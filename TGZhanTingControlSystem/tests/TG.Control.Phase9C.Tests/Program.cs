using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TG.Control.Contracts;
using TG.Control.Server;

namespace TG.Control.Phase9C.Tests;

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
            ("Draft exposes Server binding status", DraftBindingStatus),
            ("Candidate evaluation matches current draft", CandidateEvaluation),
            ("Adopt creates a Fresh generated binding", AdoptCreatesFreshBinding),
            ("Adopt does not publish content", AdoptDoesNotPublish),
            ("Changed narration text rejects old candidate", ChangedTextRejectsCandidate),
            ("Changed voice configuration rejects old candidate", ChangedConfigurationRejectsCandidate),
            ("Invalid candidate asset is rejected", InvalidAssetRejectsCandidate),
            ("Draft revision conflict is rejected", DraftRevisionConflict),
            ("Manual upload binding remains Fresh in draft", ManualUploadRemainsFresh),
            ("Legacy narration audio remains visible", LegacyAudioRemainsVisible),
            ("Draft publish uses optimistic version", DraftPublishUsesOptimisticVersion)
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
        Console.WriteLine($"Phase 9C Server tests: {tests.Length - failed}/{tests.Length} passed.");
        return failed == 0 ? 0 : 1;
    }

    private static async Task DraftBindingStatus()
    {
        await using var context = await TestContext.CreateAsync();
        var draft = await context.Workflow.GetAsync(Host, CancellationToken.None);
        Equal(NarrationAudioBindingStatus.Missing, draft.NarrationAudioStatuses.Single().Status);
    }

    private static async Task CandidateEvaluation()
    {
        await using var context = await TestContext.CreateAsync();
        var candidate = await context.GenerateAsync("Narration A");
        var evaluation = await context.Workflow.EvaluateCandidateAsync(candidate.CandidateId, Host,
            CancellationToken.None);
        True(evaluation.Adoptable, evaluation.Message);
        True(evaluation.LocationMatches && evaluation.NarrationTextMatches &&
             evaluation.SynthesisConfigurationMatches && evaluation.AssetValid,
            "Candidate evaluation did not validate every adoption dimension.");
    }

    private static async Task AdoptCreatesFreshBinding()
    {
        await using var context = await TestContext.CreateAsync();
        var candidate = await context.GenerateAsync("Narration A");
        var draft = await context.Workflow.GetAsync(Host, CancellationToken.None);
        var result = await context.Workflow.AdoptAsync(candidate.CandidateId,
            new AdoptNarrationAudioCandidateRequest(draft.BaseContentVersion, draft.Revision), "admin", Host,
            CancellationToken.None);
        Equal(NarrationAudioOrigin.Generated, result.Binding.Origin);
        Equal(NarrationAudioBindingStatus.Fresh, result.Draft.NarrationAudioStatuses.Single().Status);
        Equal(candidate.Asset.Id, result.Binding.Asset.Id);
    }

    private static async Task AdoptDoesNotPublish()
    {
        await using var context = await TestContext.CreateAsync();
        var before = await context.Published.GetAsync(CancellationToken.None);
        var candidate = await context.GenerateAsync("Narration A");
        var draft = await context.Workflow.GetAsync(Host, CancellationToken.None);
        await context.Workflow.AdoptAsync(candidate.CandidateId,
            new AdoptNarrationAudioCandidateRequest(draft.BaseContentVersion, draft.Revision), "admin", Host,
            CancellationToken.None);
        var after = await context.Published.GetAsync(CancellationToken.None);
        Equal(before.Version, after.Version);
        True(after.Modules.Single().Nodes.Single().NarrationAudio is null,
            "Adopt modified PublishedContent.");
    }

    private static async Task ChangedTextRejectsCandidate()
    {
        await using var context = await TestContext.CreateAsync();
        var candidate = await context.GenerateAsync("Narration A");
        var draft = await context.Workflow.GetAsync(Host, CancellationToken.None);
        var changed = ReplaceNode(draft.Modules, node => node with { NarrationText = "Narration B" });
        var saved = await context.Workflow.SaveAsync(new SaveContentDraftRequest(draft.BaseContentVersion,
            draft.Revision, changed), "admin", Host, CancellationToken.None);
        await ThrowsWorkflowAsync("candidate_text_stale", () => context.Workflow.AdoptAsync(candidate.CandidateId,
            new AdoptNarrationAudioCandidateRequest(saved.BaseContentVersion, saved.Revision), "admin", Host,
            CancellationToken.None));
    }

    private static async Task ChangedConfigurationRejectsCandidate()
    {
        await using var context = await TestContext.CreateAsync();
        var candidate = await context.GenerateAsync("Narration A");
        var draft = await context.Workflow.GetAsync(Host, CancellationToken.None);
        var changed = ReplaceNode(draft.Modules, node => node with
        {
            TtsConfiguration = Configuration with { Rate = 1.2 }
        });
        var saved = await context.Workflow.SaveAsync(new SaveContentDraftRequest(draft.BaseContentVersion,
            draft.Revision, changed), "admin", Host, CancellationToken.None);
        await ThrowsWorkflowAsync("candidate_configuration_stale", () => context.Workflow.AdoptAsync(
            candidate.CandidateId, new AdoptNarrationAudioCandidateRequest(saved.BaseContentVersion, saved.Revision),
            "admin", Host, CancellationToken.None));
    }

    private static async Task InvalidAssetRejectsCandidate()
    {
        await using var context = await TestContext.CreateAsync();
        var candidate = await context.GenerateAsync("Narration A");
        True(context.AssetStorage.Delete(Path.GetFileName(candidate.Asset.Url)), "Candidate asset was not deleted.");
        var draft = await context.Workflow.GetAsync(Host, CancellationToken.None);
        await ThrowsWorkflowAsync("candidate_asset_invalid", () => context.Workflow.AdoptAsync(candidate.CandidateId,
            new AdoptNarrationAudioCandidateRequest(draft.BaseContentVersion, draft.Revision), "admin", Host,
            CancellationToken.None));
    }

    private static async Task DraftRevisionConflict()
    {
        await using var context = await TestContext.CreateAsync();
        var draft = await context.Workflow.GetAsync(Host, CancellationToken.None);
        await context.Workflow.SaveAsync(new SaveContentDraftRequest(draft.BaseContentVersion, draft.Revision,
            draft.Modules), "admin-a", Host, CancellationToken.None);
        await ThrowsWorkflowAsync("draft_revision_conflict", () => context.Workflow.SaveAsync(
            new SaveContentDraftRequest(draft.BaseContentVersion, draft.Revision, draft.Modules),
            "admin-b", Host, CancellationToken.None));
    }

    private static async Task ManualUploadRemainsFresh()
    {
        await using var context = await TestContext.CreateAsync();
        await using var stream = new MemoryStream(CreateWave(), false);
        var asset = await context.AssetStorage.ImportValidatedAudioAsync(stream, "manual.wav", "audio/wav", .25,
            CancellationToken.None);
        var bindingService = new NarrationAudioBindingService(context.AssetStorage);
        var binding = bindingService.CreateManualBinding(
            new CreateManualNarrationAudioBindingRequest(asset, "Narration A"), Host);
        var draft = await context.Workflow.GetAsync(Host, CancellationToken.None);
        var modules = ReplaceNode(draft.Modules, node => node with
        {
            TtsAudioUrl = binding.Asset.Url,
            TtsConfiguration = binding.SynthesisConfiguration,
            NarrationAudio = binding
        });
        var saved = await context.Workflow.SaveAsync(new SaveContentDraftRequest(draft.BaseContentVersion,
            draft.Revision, modules), "admin", Host, CancellationToken.None);
        Equal(NarrationAudioBindingStatus.Fresh, saved.NarrationAudioStatuses.Single().Status);
        Equal(NarrationAudioOrigin.ManualUpload,
            saved.Modules.Single().Nodes.Single().NarrationAudio!.Origin);
    }

    private static async Task LegacyAudioRemainsVisible()
    {
        await using var context = await TestContext.CreateAsync(legacy: true);
        var draft = await context.Workflow.GetAsync(Host, CancellationToken.None);
        Equal(NarrationAudioBindingStatus.LegacyUnverified, draft.NarrationAudioStatuses.Single().Status);
        True(!string.IsNullOrWhiteSpace(draft.Modules.Single().Nodes.Single().TtsAudioUrl),
            "Legacy URL was removed from the draft.");
    }

    private static async Task DraftPublishUsesOptimisticVersion()
    {
        await using var context = await TestContext.CreateAsync();
        var draft = await context.Workflow.GetAsync(Host, CancellationToken.None);
        var published = await context.Workflow.PublishAsync(new SaveContentDraftRequest(draft.BaseContentVersion,
            draft.Revision, draft.Modules), "admin", Host, CancellationToken.None);
        Equal(draft.BaseContentVersion + 1, published.Version);
        await ThrowsWorkflowAsync("content_version_conflict", () => context.Workflow.PublishAsync(
            new SaveContentDraftRequest(draft.BaseContentVersion, draft.Revision, draft.Modules),
            "admin", Host, CancellationToken.None));
    }

    private static IReadOnlyList<ExhibitionModule> ReplaceNode(IReadOnlyList<ExhibitionModule> modules,
        Func<NarrationNode, NarrationNode> update)
    {
        var module = modules.Single();
        return [module with { Nodes = [update(module.Nodes.Single())] }];
    }

    private static async Task ThrowsWorkflowAsync(string code, Func<Task> action)
    {
        try { await action(); }
        catch (ContentDraftWorkflowException exception)
        {
            Equal(code, exception.ErrorCode);
            return;
        }
        throw new InvalidOperationException($"Expected workflow error '{code}'.");
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

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
    }

    private static void True(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private sealed class TestContext : IAsyncDisposable
    {
        private TestContext(string rootPath, AssetStorage assetStorage, JsonContentRepository published,
            TtsProductionService tts, ContentDraftWorkflowService workflow)
        {
            RootPath = rootPath;
            AssetStorage = assetStorage;
            Published = published;
            Tts = tts;
            Workflow = workflow;
        }

        public string RootPath { get; }
        public AssetStorage AssetStorage { get; }
        public JsonContentRepository Published { get; }
        public TtsProductionService Tts { get; }
        public ContentDraftWorkflowService Workflow { get; }

        public static async Task<TestContext> CreateAsync(bool legacy = false)
        {
            var root = Path.Combine(Path.GetTempPath(), "tg-phase9c-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var environment = new TestEnvironment(root);
            var storageOptions = Microsoft.Extensions.Options.Options.Create(new StorageOptions { DataDirectory = "Data" });
            var productionOptions = Microsoft.Extensions.Options.Options.Create(new TtsProductionOptions
            {
                MaxAttempts = 2,
                AttemptTimeoutMilliseconds = 500,
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
            var validator = new TtsMediaValidator(productionOptions, storageOptions, environment);
            var tts = new TtsProductionService(new TtsProviderRegistry([new DeterministicTestTtsProvider()]),
                ttsRepository, validator, assets, productionOptions, NullLogger<TtsProductionService>.Instance);
            var eventRepository = new OperationalEventRepository(storageOptions, environment);
            var workflow = new ContentDraftWorkflowService(published,
                new ContentDraftRepository(storageOptions, environment), tts, assets, eventRepository);
            return new TestContext(root, assets, published, tts, workflow);
        }

        public async Task<NarrationAudioCandidate> GenerateAsync(string text)
        {
            var created = await Tts.CreateAsync(new CreateTtsProductionJobRequest("module", "node", text,
                Configuration), "admin", CancellationToken.None);
            var job = await WaitForTerminalAsync(Tts, created.Job.JobId);
            Equal(TtsProductionJobStatus.Succeeded, job.Status);
            return await Tts.GetCandidateAsync(job.CandidateId!, CancellationToken.None)
                   ?? throw new InvalidOperationException("Candidate was not created.");
        }

        public async ValueTask DisposeAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await Tts.StopAsync(timeout.Token);
            if (Directory.Exists(RootPath)) Directory.Delete(RootPath, true);
        }
    }

    private sealed class TestEnvironment(string contentRootPath) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "TG.Control.Phase9C.Tests";
        public string ContentRootPath { get; set; } = contentRootPath;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
