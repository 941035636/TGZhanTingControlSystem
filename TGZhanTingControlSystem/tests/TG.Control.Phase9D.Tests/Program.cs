using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TG.Control.Contracts;
using TG.Control.Server;

namespace TG.Control.Phase9D.Tests;

internal static class Program
{
    private static readonly HostString Host = new("localhost:5080");
    private static readonly TtsSynthesisConfiguration Configuration = new(
        DeterministicTestTtsProvider.Id, DeterministicTestTtsProvider.VoiceId,
        "zh-CN", 1, 0, 1, "audio/wav", 8000, 1);

    private static async Task<int> Main()
    {
        var tests = new (string Name, Func<Task> Run)[]
        {
            ("Fresh binding publishes successfully", FreshBindingPublishes),
            ("Stale text is rejected", StaleTextRejected),
            ("Stale synthesis configuration is rejected", StaleConfigurationRejected),
            ("Invalid AssetId is rejected", InvalidAssetIdRejected),
            ("Wrong SHA is rejected", WrongShaRejected),
            ("Wrong size is rejected", WrongSizeRejected),
            ("Missing asset file is rejected", MissingAssetFileRejected),
            ("Unsupported narration media type is rejected", UnsupportedMediaTypeRejected),
            ("Invalid binding fingerprint is rejected", InvalidBindingRejected),
            ("Candidate without Adopt never enters PublishedContent", CandidateWithoutAdoptIsNotPublished),
            ("Running Generate does not enter PublishedContent", RunningGenerateDoesNotEnterPublished),
            ("Published snapshot is isolated from later draft changes", PublishedSnapshotIsImmutable),
            ("Manifest includes complete narration asset identity", ManifestHasCompleteIdentity),
            ("Manifest deduplicates the same immutable asset", ManifestDeduplicatesAsset),
            ("Manifest rejects conflicting immutable asset identity", ManifestRejectsIdentityConflict),
            ("Rollback restores historical binding and manifest", RollbackRestoresBindingAndManifest),
            ("Failed publish is atomic", FailedPublishIsAtomic),
            ("Stale draft revision is rejected", RevisionConflictRejected),
            ("Concurrent publish permits exactly one winner", ConcurrentPublishHasOneWinner),
            ("Adopt and Publish cannot silently cross", AdoptPublishConcurrencyIsSafe),
            ("Legacy content reads, publishes, rolls back and plays", LegacyCompatibility),
            ("Video plus narration text without audio is an explicit warning", VideoWithoutAudioWarns),
            ("Pure narration text without audio blocks publish", PureNarrationWithoutAudioBlocks),
            ("Referenced assets are protected from deletion", ReferencedAssetsAreProtected),
            ("Rollback requires current revision", RollbackRevisionConflictRejected)
        };

        var failed = 0;
        foreach (var test in tests)
        {
            try
            {
                await test.Run();
                Console.WriteLine($"PASS  {test.Name}");
            }
            catch (Exception exception)
            {
                failed++;
                Console.Error.WriteLine($"FAIL  {test.Name}: {exception}");
            }
        }
        Console.WriteLine($"Phase 9D tests: {tests.Length - failed}/{tests.Length} passed.");
        return failed == 0 ? 0 : 1;
    }

    private static async Task FreshBindingPublishes()
    {
        await using var context = await TestContext.CreateAsync();
        var result = await context.PublishTextAsync("Narration A");
        var node = result.Published.Modules.Single().Nodes.Single();
        Equal(NarrationAudioOrigin.Generated, node.NarrationAudio!.Origin);
        Equal(result.Candidate.Asset.Id, node.NarrationAudio.Asset.Id);
        Equal(NarrationAudioBindingStatus.Fresh,
            NarrationAudioBindingInspector.Evaluate(node, context.Assets, Host).Status);
    }

    private static async Task StaleTextRejected()
    {
        await using var context = await TestContext.CreateAsync();
        var version = (await context.PublishTextAsync("Narration A")).Published.Version;
        var draft = await context.Workflow.GetAsync(Host, CancellationToken.None);
        var changed = ReplaceNode(draft.Modules, node => node with { NarrationText = "Narration B" });
        var saved = await context.SaveDraftAsync(draft, changed);
        await ExpectValidationAsync(() => context.PublishAsync(saved), "讲解词");
        Equal(version, (await context.Published.GetAsync(CancellationToken.None)).Version);
    }

    private static async Task StaleConfigurationRejected()
    {
        await using var context = await TestContext.CreateAsync();
        var version = (await context.PublishTextAsync("Narration A")).Published.Version;
        var draft = await context.Workflow.GetAsync(Host, CancellationToken.None);
        var changed = ReplaceNode(draft.Modules, node => node with
        {
            TtsConfiguration = node.TtsConfiguration! with { Rate = 1.2 }
        });
        var saved = await context.SaveDraftAsync(draft, changed);
        await ExpectValidationAsync(() => context.PublishAsync(saved), "合成");
        Equal(version, (await context.Published.GetAsync(CancellationToken.None)).Version);
    }

    private static async Task InvalidAssetIdRejected() =>
        await InvalidBindingAssetRejected(binding => binding with { Asset = binding.Asset with { Id = string.Empty } },
            "资产ID");

    private static async Task WrongShaRejected() =>
        await InvalidBindingAssetRejected(binding => binding with
        {
            Asset = binding.Asset with { Sha256 = new string('0', 64) }
        }, "SHA-256");

    private static async Task WrongSizeRejected() =>
        await InvalidBindingAssetRejected(binding => binding with
        {
            Asset = binding.Asset with { SizeBytes = binding.Asset.SizeBytes + 1 }
        }, "大小");

    private static async Task MissingAssetFileRejected() =>
        await InvalidBindingAssetRejected(binding => binding with
        {
            Asset = binding.Asset with
            {
                Id = Guid.NewGuid().ToString("N"),
                Url = "/media/missing-phase9d.wav"
            }
        }, "HTTP 404");

    private static async Task UnsupportedMediaTypeRejected() =>
        await InvalidBindingAssetRejected(binding => binding with
        {
            Asset = binding.Asset with { MediaType = "audio/aac" }
        }, "媒体格式");

    private static async Task InvalidBindingRejected() =>
        await InvalidBindingAssetRejected(binding => binding with
        {
            NarrationTextFingerprint = "not-a-sha256"
        }, "指纹");

    private static async Task InvalidBindingAssetRejected(Func<NarrationAudioBinding, NarrationAudioBinding> mutate,
        string expectedMessage)
    {
        await using var context = await TestContext.CreateAsync();
        var adopted = await context.PrepareAdoptedDraftAsync("Narration A");
        var changed = ReplaceNode(adopted.Draft.Modules, node =>
        {
            var binding = mutate(node.NarrationAudio!);
            return node with { NarrationAudio = binding, TtsAudioUrl = binding.Asset.Url };
        });
        var saved = await context.SaveDraftAsync(adopted.Draft, changed);
        await ExpectValidationAsync(() => context.PublishAsync(saved), expectedMessage);
        Equal(1L, (await context.Published.GetAsync(CancellationToken.None)).Version);
    }

    private static async Task CandidateWithoutAdoptIsNotPublished()
    {
        await using var context = await TestContext.CreateAsync();
        var before = await context.Published.GetAsync(CancellationToken.None);
        var candidate = await context.GenerateAsync("Narration B");
        var after = await context.Published.GetAsync(CancellationToken.None);
        Equal(before.Version, after.Version);
        True(after.Modules.SelectMany(module => module.Nodes).All(node => node.NarrationAudio is null),
            "Generate modified PublishedContent.");
        True(ContentManifestBuilder.Build(after).Assets.All(asset => asset.AssetId != candidate.Asset.Id),
            "Unadopted Candidate entered the LED manifest.");
    }

    private static async Task RunningGenerateDoesNotEnterPublished()
    {
        await using var context = await TestContext.CreateAsync(includeBlockingProvider: true);
        var publishedA = await context.PublishTextAsync("Narration A");
        var blockingConfiguration = Configuration with
        {
            ProviderKey = BlockingTtsProvider.Id,
            Voice = BlockingTtsProvider.VoiceId
        };
        var created = await context.Tts.CreateAsync(new CreateTtsProductionJobRequest("module", "node",
            "Future narration", blockingConfiguration), "admin", CancellationToken.None);
        await WaitForStatusAsync(context.Tts, created.Job.JobId, TtsProductionJobStatus.Running);
        var draft = await context.Workflow.GetAsync(Host, CancellationToken.None);
        var republished = await context.PublishAsync(draft);
        Equal(publishedA.Candidate.Asset.Id,
            republished.Modules.Single().Nodes.Single().NarrationAudio!.Asset.Id);
        await context.Tts.CancelAsync(created.Job.JobId, CancellationToken.None);
    }

    private static async Task PublishedSnapshotIsImmutable()
    {
        await using var context = await TestContext.CreateAsync();
        var publishedA = await context.PublishTextAsync("Narration A");
        var draft = await context.Workflow.GetAsync(Host, CancellationToken.None);
        var changed = ReplaceNode(draft.Modules, node => node with { NarrationText = "Narration B" });
        await context.SaveDraftAsync(draft, changed);
        var current = await context.Published.GetAsync(CancellationToken.None);
        var currentNode = current.Modules.Single().Nodes.Single();
        Equal("Narration A", currentNode.NarrationText);
        Equal(publishedA.Candidate.Asset.Id, currentNode.NarrationAudio!.Asset.Id);
    }

    private static async Task ManifestHasCompleteIdentity()
    {
        await using var context = await TestContext.CreateAsync();
        var result = await context.PublishTextAsync("Narration A");
        var asset = ContentManifestBuilder.Build(result.Published).Assets.Single();
        Equal(result.Candidate.Asset.Id, asset.AssetId);
        Equal(result.Candidate.Asset.Sha256, asset.Sha256);
        Equal(result.Candidate.Asset.SizeBytes, asset.SizeBytes);
        Equal("audio/wav", asset.MediaType);
    }

    private static async Task ManifestDeduplicatesAsset()
    {
        await using var context = await TestContext.CreateAsync();
        var adopted = await context.PrepareAdoptedDraftAsync("Narration A");
        var module = adopted.Draft.Modules.Single();
        var first = module.Nodes.Single();
        var second = first with { Id = "node-2", Name = "Node 2", Order = 2 };
        var modules = new[] { module with { Nodes = [first, second] } };
        var saved = await context.SaveDraftAsync(adopted.Draft, modules);
        var published = await context.PublishAsync(saved);
        var manifest = ContentManifestBuilder.Build(published);
        Equal(1, manifest.Assets.Count(asset => asset.AssetId == adopted.Candidate.Asset.Id));
    }

    private static async Task ManifestRejectsIdentityConflict()
    {
        await using var context = await TestContext.CreateAsync();
        var adopted = await context.PrepareAdoptedDraftAsync("Narration A");
        var module = adopted.Draft.Modules.Single();
        var first = module.Nodes.Single();
        var conflictingAsset = first.NarrationAudio!.Asset with { Url = "/media/conflicting.wav" };
        var second = first with
        {
            Id = "node-2",
            Name = "Node 2",
            Order = 2,
            NarrationAudio = first.NarrationAudio with { Asset = conflictingAsset },
            TtsAudioUrl = conflictingAsset.Url
        };
        var content = new PublishedContent(2, DateTimeOffset.UtcNow, "test",
            [module with { Nodes = [first, second] }]);
        try { _ = ContentManifestBuilder.Build(content); }
        catch (InvalidDataException exception)
        {
            True(exception.Message.Contains("身份冲突", StringComparison.Ordinal),
                "Manifest identity conflict did not explain the failure.");
            return;
        }
        throw new InvalidOperationException("Manifest accepted conflicting immutable asset identity.");
    }

    private static async Task RollbackRestoresBindingAndManifest()
    {
        await using var context = await TestContext.CreateAsync();
        var versionA = await context.PublishTextAsync("Narration A");
        var versionB = await context.PublishTextAsync("Narration B");
        var draft = await context.Workflow.GetAsync(Host, CancellationToken.None);
        var restoredA = await context.Workflow.RollbackAsync(versionA.Published.Version,
            new RollbackContentRequest(versionB.Published.Version, draft.Revision), "admin", Host,
            CancellationToken.None);
        var nodeA = restoredA.Modules.Single().Nodes.Single();
        Equal("Narration A", nodeA.NarrationText);
        Equal(versionA.Candidate.Asset.Id, nodeA.NarrationAudio!.Asset.Id);
        Equal(versionA.Candidate.Asset.Id, ContentManifestBuilder.Build(restoredA).Assets.Single().AssetId);

        var restoredDraft = await context.Workflow.GetAsync(Host, CancellationToken.None);
        var restoredB = await context.Workflow.RollbackAsync(versionB.Published.Version,
            new RollbackContentRequest(restoredA.Version, restoredDraft.Revision), "admin", Host,
            CancellationToken.None);
        Equal("Narration B", restoredB.Modules.Single().Nodes.Single().NarrationText);
        Equal(versionB.Candidate.Asset.Id, ContentManifestBuilder.Build(restoredB).Assets.Single().AssetId);
    }

    private static async Task FailedPublishIsAtomic()
    {
        await using var context = await TestContext.CreateAsync();
        var published = await context.PublishTextAsync("Narration A");
        var draft = await context.Workflow.GetAsync(Host, CancellationToken.None);
        var changed = ReplaceNode(draft.Modules, node => node with
        {
            NarrationAudio = node.NarrationAudio! with
            {
                Asset = node.NarrationAudio.Asset with { Sha256 = new string('f', 64) }
            }
        });
        var saved = await context.SaveDraftAsync(draft, changed);
        await ExpectValidationAsync(() => context.PublishAsync(saved), "SHA-256");
        var after = await context.Published.GetAsync(CancellationToken.None);
        Equal(published.Published.Version, after.Version);
        Equal(published.Candidate.Asset.Id, after.Modules.Single().Nodes.Single().NarrationAudio!.Asset.Id);
    }

    private static async Task RevisionConflictRejected()
    {
        await using var context = await TestContext.CreateAsync();
        var adopted = await context.PrepareAdoptedDraftAsync("Narration A");
        await context.SaveDraftAsync(adopted.Draft, adopted.Draft.Modules);
        await ExpectWorkflowAsync("draft_revision_conflict", () => context.PublishAsync(adopted.Draft));
    }

    private static async Task ConcurrentPublishHasOneWinner()
    {
        await using var context = await TestContext.CreateAsync();
        var adopted = await context.PrepareAdoptedDraftAsync("Narration A");
        var first = CaptureAsync(() => context.PublishAsync(adopted.Draft));
        var second = CaptureAsync(() => context.PublishAsync(adopted.Draft));
        var results = await Task.WhenAll(first, second);
        Equal(1, results.Count(result => result.Success));
        Equal(1, results.Count(result => result.ErrorCode == "content_version_conflict"));
    }

    private static async Task AdoptPublishConcurrencyIsSafe()
    {
        await using var context = await TestContext.CreateAsync();
        var publishedA = await context.PublishTextAsync("Narration A");
        var draft = await context.Workflow.GetAsync(Host, CancellationToken.None);
        var adopt = CaptureAsync(async () =>
        {
            await context.Workflow.AdoptAsync(publishedA.Candidate.CandidateId,
                new AdoptNarrationAudioCandidateRequest(draft.BaseContentVersion, draft.Revision), "admin-a", Host,
                CancellationToken.None);
            return true;
        });
        var publish = CaptureAsync(async () =>
        {
            await context.PublishAsync(draft);
            return true;
        });
        var outcomes = await Task.WhenAll(adopt, publish);
        Equal(1, outcomes.Count(result => result.Success));
        Equal(1, outcomes.Count(result => result.ErrorCode is "draft_revision_conflict" or "content_version_conflict"));
    }

    private static async Task LegacyCompatibility()
    {
        await using var context = await TestContext.CreateAsync(legacy: true);
        var draft = await context.Workflow.GetAsync(Host, CancellationToken.None);
        Equal(1, draft.PublishReadiness!.NarrationAudio.LegacyUnverified);
        True(draft.PublishReadiness.CanPublish, "Unchanged legacy content should remain publishable.");
        var legacyUrl = draft.Modules.Single().Nodes.Single().TtsAudioUrl;
        var republished = await context.PublishAsync(draft);

        var fresh = await context.PublishTextAsync("Narration B");
        var rollbackDraft = await context.Workflow.GetAsync(Host, CancellationToken.None);
        var restored = await context.Workflow.RollbackAsync(republished.Version,
            new RollbackContentRequest(fresh.Published.Version, rollbackDraft.Revision), "admin", Host,
            CancellationToken.None);
        Equal(legacyUrl, restored.Modules.Single().Nodes.Single().TtsAudioUrl);
        True(restored.Modules.Single().Nodes.Single().NarrationAudio is null,
            "Legacy rollback was incorrectly upgraded to Fresh.");

        var broker = new CommandBroker();
        var coordinator = new PlaybackCoordinator(context.Published, broker,
            Options.Create(new PlaybackOptions { RequireLedReadyBeforeStart = false, LedClientId = "led-main" }),
            new PlaybackSessionStore(context.StorageOptions, context.Environment), context.Events,
            NullLogger<PlaybackCoordinator>.Instance);
        await coordinator.StartAsync(new StartNarrationRequest(["module"], "test"), CancellationToken.None);
        var command = await broker.WaitAsync("led-main", TimeSpan.FromSeconds(1), CancellationToken.None);
        Equal(legacyUrl, command!.NarrationAudioUrl);
    }

    private static async Task VideoWithoutAudioWarns()
    {
        await using var context = await TestContext.CreateAsync();
        var draft = await context.Workflow.GetAsync(Host, CancellationToken.None);
        var video = context.CreateVisualAsset("video.mp4");
        var modules = ReplaceNode(draft.Modules, node => node with
        {
            Assets = [video], NarrationAudio = null, TtsAudioUrl = null
        });
        var saved = await context.SaveDraftAsync(draft, modules);
        True(saved.PublishReadiness!.CanPublish, "Video-only compatibility warning blocked publish.");
        True(saved.PublishReadiness.Issues.Any(issue => issue.Code == "video_without_narration_audio" &&
                                                       issue.Severity == ContentPublishIssueSeverity.Warning),
            "Video-only warning was not reported.");
        _ = await context.PublishAsync(saved);
    }

    private static async Task PureNarrationWithoutAudioBlocks()
    {
        await using var context = await TestContext.CreateAsync();
        var draft = await context.Workflow.GetAsync(Host, CancellationToken.None);
        True(!draft.PublishReadiness!.CanPublish, "Pure narration without audio was marked publishable.");
        True(draft.PublishReadiness.Issues.Any(issue => issue.Code == "narration_audio_missing"),
            "Missing narration audio was not reported.");
        await ExpectValidationAsync(() => context.PublishAsync(draft), "没有视频或可播放讲解音频");
    }

    private static async Task ReferencedAssetsAreProtected()
    {
        await using var context = await TestContext.CreateAsync();
        var candidate = await context.GenerateAsync("Candidate only");
        var candidateDelete = await context.Protection.DeleteIfUnreferencedAsync(
            Path.GetFileName(candidate.Asset.Url), CancellationToken.None);
        True(candidateDelete.Protected && candidateDelete.References.Any(value => value.Contains("候选")),
            "Valid Candidate asset was not protected.");

        var adopted = await context.PrepareAdoptedDraftAsync("Narration A");
        var draftDelete = await context.Protection.DeleteIfUnreferencedAsync(
            Path.GetFileName(adopted.Candidate.Asset.Url), CancellationToken.None);
        True(draftDelete.Protected && draftDelete.References.Any(value => value.Contains("草稿")),
            "Draft binding asset was not protected.");
        var publishedA = await context.PublishAsync(adopted.Draft);
        _ = await context.PublishTextAsync("Narration B");
        var historicalDelete = await context.Protection.DeleteIfUnreferencedAsync(
            Path.GetFileName(adopted.Candidate.Asset.Url), CancellationToken.None);
        True(historicalDelete.Protected && historicalDelete.References.Any(value =>
                value.Contains($"V{publishedA.Version}", StringComparison.Ordinal)),
            "Historical rollback asset was not protected.");
    }

    private static async Task RollbackRevisionConflictRejected()
    {
        await using var context = await TestContext.CreateAsync();
        var versionA = await context.PublishTextAsync("Narration A");
        var versionB = await context.PublishTextAsync("Narration B");
        var draft = await context.Workflow.GetAsync(Host, CancellationToken.None);
        await context.SaveDraftAsync(draft, draft.Modules);
        await ExpectWorkflowAsync("draft_revision_conflict", () => context.Workflow.RollbackAsync(
            versionA.Published.Version, new RollbackContentRequest(versionB.Published.Version, draft.Revision),
            "admin", Host, CancellationToken.None));
    }

    private static IReadOnlyList<ExhibitionModule> ReplaceNode(IReadOnlyList<ExhibitionModule> modules,
        Func<NarrationNode, NarrationNode> update)
    {
        var module = modules.Single();
        return [module with { Nodes = [update(module.Nodes.Single())] }];
    }

    private static async Task ExpectValidationAsync(Func<Task> action, string messagePart)
    {
        try { await action(); }
        catch (ContentDraftValidationException exception)
        {
            True(exception.Errors.Values.SelectMany(value => value).Any(message =>
                    message.Contains(messagePart, StringComparison.OrdinalIgnoreCase)),
                $"Validation did not contain '{messagePart}'.");
            return;
        }
        throw new InvalidOperationException("Expected ContentDraftValidationException.");
    }

    private static async Task ExpectWorkflowAsync(string code, Func<Task> action)
    {
        try { await action(); }
        catch (ContentDraftWorkflowException exception)
        {
            Equal(code, exception.ErrorCode);
            return;
        }
        throw new InvalidOperationException($"Expected workflow error '{code}'.");
    }

    private static async Task<(bool Success, string? ErrorCode)> CaptureAsync(Func<Task> action)
    {
        try { await action(); return (true, null); }
        catch (ContentDraftWorkflowException exception) { return (false, exception.ErrorCode); }
    }

    private static async Task<(bool Success, string? ErrorCode)> CaptureAsync<T>(Func<Task<T>> action)
    {
        try { _ = await action(); return (true, null); }
        catch (ContentDraftWorkflowException exception) { return (false, exception.ErrorCode); }
    }

    private static async Task<TtsProductionJob> WaitForTerminalAsync(TtsProductionService service, string jobId)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (true)
        {
            var job = await service.GetJobAsync(jobId, timeout.Token)
                      ?? throw new InvalidOperationException("Job disappeared.");
            if (job.Status is TtsProductionJobStatus.Succeeded or TtsProductionJobStatus.Failed or
                TtsProductionJobStatus.Cancelled) return job;
            await Task.Delay(10, timeout.Token);
        }
    }

    private static async Task WaitForStatusAsync(TtsProductionService service, string jobId,
        TtsProductionJobStatus expected)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (true)
        {
            var job = await service.GetJobAsync(jobId, timeout.Token)
                      ?? throw new InvalidOperationException("Job disappeared.");
            if (job.Status == expected) return;
            if (job.Status is TtsProductionJobStatus.Succeeded or TtsProductionJobStatus.Failed or
                TtsProductionJobStatus.Cancelled)
                throw new InvalidOperationException($"Job reached {job.Status} before {expected}.");
            await Task.Delay(10, timeout.Token);
        }
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
    }

    private static void True(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private sealed record PublishResult(NarrationAudioCandidate Candidate, PublishedContent Published);
    private sealed record AdoptedDraft(NarrationAudioCandidate Candidate, ContentDraftSnapshot Draft);

    private sealed class TestContext : IAsyncDisposable
    {
        private TestContext(string rootPath, TestEnvironment environment, IOptions<StorageOptions> storageOptions,
            AssetStorage assets, JsonContentRepository published, TtsProductionRepository ttsRepository,
            TtsProductionService tts, ContentDraftRepository draftRepository,
            ContentDraftWorkflowService workflow, OperationalEventRepository events,
            AssetReferenceProtectionService protection)
        {
            RootPath = rootPath;
            Environment = environment;
            StorageOptions = storageOptions;
            Assets = assets;
            Published = published;
            TtsRepository = ttsRepository;
            Tts = tts;
            DraftRepository = draftRepository;
            Workflow = workflow;
            Events = events;
            Protection = protection;
        }

        public string RootPath { get; }
        public TestEnvironment Environment { get; }
        public IOptions<StorageOptions> StorageOptions { get; }
        public AssetStorage Assets { get; }
        public JsonContentRepository Published { get; }
        public TtsProductionRepository TtsRepository { get; }
        public TtsProductionService Tts { get; }
        public ContentDraftRepository DraftRepository { get; }
        public ContentDraftWorkflowService Workflow { get; }
        public OperationalEventRepository Events { get; }
        public AssetReferenceProtectionService Protection { get; }

        public static async Task<TestContext> CreateAsync(bool legacy = false, bool includeBlockingProvider = false)
        {
            var root = Path.Combine(Path.GetTempPath(), "TG.Control.Phase9D.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var environment = new TestEnvironment(root);
            var storageOptions = Options.Create(new StorageOptions { DataDirectory = "Data" });
            var productionOptions = Options.Create(new TtsProductionOptions
            {
                MaxAttempts = 2,
                AttemptTimeoutMilliseconds = 5000,
                RetryDelayMilliseconds = 1,
                MaxTextLength = 5000,
                MinAudioSizeBytes = 45,
                MaxAudioSizeBytes = 1024 * 1024
            });
            var assets = new AssetStorage(storageOptions, environment);
            var published = new JsonContentRepository(storageOptions, environment);
            string? legacyUrl = null;
            if (legacy)
            {
                await using var source = new MemoryStream(CreateWave(), false);
                legacyUrl = (await assets.ImportValidatedAudioAsync(source, "legacy.wav", "audio/wav", .25,
                    CancellationToken.None)).Url;
            }
            var node = new NarrationNode("node", "Node", 1, "Narration A", legacyUrl, [],
                TtsConfiguration: legacy ? null : Configuration);
            await published.SaveAsync([new ExhibitionModule("module", "Module", 1, string.Empty, null, true, [node])],
                "seed", CancellationToken.None);

            var ttsRepository = new TtsProductionRepository(storageOptions, environment);
            var providers = new List<ITtsProvider> { new DeterministicTestTtsProvider() };
            if (includeBlockingProvider) providers.Add(new BlockingTtsProvider());
            var tts = new TtsProductionService(new TtsProviderRegistry(providers), ttsRepository,
                new TtsMediaValidator(productionOptions, storageOptions, environment), assets, productionOptions,
                NullLogger<TtsProductionService>.Instance);
            var drafts = new ContentDraftRepository(storageOptions, environment);
            var events = new OperationalEventRepository(storageOptions, environment);
            var workflow = new ContentDraftWorkflowService(published, drafts, tts, assets, events);
            var protection = new AssetReferenceProtectionService(published, drafts, ttsRepository, assets);
            return new TestContext(root, environment, storageOptions, assets, published, ttsRepository, tts,
                drafts, workflow, events, protection);
        }

        public async Task<NarrationAudioCandidate> GenerateAsync(string text,
            TtsSynthesisConfiguration? configuration = null)
        {
            var created = await Tts.CreateAsync(new CreateTtsProductionJobRequest("module", "node", text,
                configuration ?? Configuration), "admin", CancellationToken.None);
            var job = await WaitForTerminalAsync(Tts, created.Job.JobId);
            Equal(TtsProductionJobStatus.Succeeded, job.Status);
            return await Tts.GetCandidateAsync(job.CandidateId!, CancellationToken.None)
                   ?? throw new InvalidOperationException("Candidate was not created.");
        }

        public async Task<AdoptedDraft> PrepareAdoptedDraftAsync(string text,
            TtsSynthesisConfiguration? configuration = null)
        {
            var synthesis = configuration ?? Configuration;
            var draft = await Workflow.GetAsync(Host, CancellationToken.None);
            var modules = ReplaceNode(draft.Modules, node => node with
            {
                NarrationText = text,
                TtsAudioUrl = null,
                TtsConfiguration = synthesis,
                NarrationAudio = null
            });
            var saved = await SaveDraftAsync(draft, modules);
            var candidate = await GenerateAsync(text, synthesis);
            var adopted = await Workflow.AdoptAsync(candidate.CandidateId,
                new AdoptNarrationAudioCandidateRequest(saved.BaseContentVersion, saved.Revision), "admin", Host,
                CancellationToken.None);
            return new AdoptedDraft(candidate, adopted.Draft);
        }

        public async Task<PublishResult> PublishTextAsync(string text)
        {
            var adopted = await PrepareAdoptedDraftAsync(text);
            return new PublishResult(adopted.Candidate, await PublishAsync(adopted.Draft));
        }

        public Task<ContentDraftSnapshot> SaveDraftAsync(ContentDraftSnapshot draft,
            IReadOnlyList<ExhibitionModule> modules) => Workflow.SaveAsync(
            new SaveContentDraftRequest(draft.BaseContentVersion, draft.Revision, modules), "admin", Host,
            CancellationToken.None);

        public Task<PublishedContent> PublishAsync(ContentDraftSnapshot draft) => Workflow.PublishAsync(
            new SaveContentDraftRequest(draft.BaseContentVersion, draft.Revision, draft.Modules), "admin", Host,
            CancellationToken.None);

        public ContentAsset CreateVisualAsset(string name)
        {
            var bytes = "phase-9d-video"u8.ToArray();
            var path = Path.Combine(Assets.MediaDirectory, name);
            File.WriteAllBytes(path, bytes);
            return new ContentAsset(Guid.NewGuid().ToString("N"), name, AssetKind.Video, "/media/" + name,
                Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), bytes.Length, 1, "video/mp4");
        }

        public async ValueTask DisposeAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await Tts.StopAsync(timeout.Token);
            var allowedParent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "TG.Control.Phase9D.Tests"));
            var resolved = Path.GetFullPath(RootPath);
            if (resolved.StartsWith(allowedParent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                Directory.Exists(resolved))
                Directory.Delete(resolved, true);
        }
    }

    private sealed class BlockingTtsProvider : ITtsProvider
    {
        public const string Id = "blocking-test";
        public const string VoiceId = "blocking-voice";
        public string ProviderId => Id;

        public Task<TtsProviderDescriptor> GetDescriptorAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new TtsProviderDescriptor(Id, "Blocking test provider", true, true, null,
                [new TtsVoiceDescriptor(VoiceId, "Blocking voice", "zh-CN")],
                new TtsProviderCapabilities(5000, .5, 2, -1, 1, ["audio/wav"])));

        public async Task<TtsProviderAudioResult> SynthesizeAsync(TtsProviderSynthesisRequest request,
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Unreachable.");
        }
    }

    private static byte[] CreateWave()
    {
        const int sampleRate = 8000;
        const int samples = 2000;
        var dataSize = samples * 2;
        using var stream = new MemoryStream(44 + dataSize);
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.ASCII, true))
        {
            writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + dataSize);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)1);
            writer.Write(sampleRate);
            writer.Write(sampleRate * 2);
            writer.Write((short)2);
            writer.Write((short)16);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
            writer.Write(dataSize);
            for (var index = 0; index < samples; index++) writer.Write((short)(index % 97));
        }
        return stream.ToArray();
    }

    public sealed class TestEnvironment(string contentRootPath) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "TG.Control.Phase9D.Tests";
        public string ContentRootPath { get; set; } = contentRootPath;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
