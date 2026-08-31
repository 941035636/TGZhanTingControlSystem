using System.Security.Cryptography;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TG.Control.Contracts;
using TG.Control.Server;

namespace TG.Control.Phase9B.Tests;

internal static class Program
{
    private static readonly TtsSynthesisConfiguration Configuration = new(
        "test", "voice", "zh-CN", 1, 0, 1, "audio/wav", 8000, 1);

    private static async Task<int> Main()
    {
        var tests = new (string Name, Func<Task> Run)[]
        {
            ("Deterministic development provider is stable", DeterministicProviderIsStable),
            ("Normal generation and state transition", NormalGeneration),
            ("Candidate asset SHA and size are authoritative", AssetIntegrity),
            ("Sequential duplicate request is idempotent", SequentialIdempotency),
            ("Concurrent duplicate request is idempotent", ConcurrentIdempotency),
            ("Permanent provider failure is not retried", PermanentFailure),
            ("Transient provider failure has finite retries", TransientFailureHasFiniteRetries),
            ("Provider timeout has finite retries", ProviderTimeout),
            ("Empty provider media is rejected", EmptyMedia),
            ("Corrupt provider media is rejected", CorruptMedia),
            ("Invalid provider is rejected", InvalidProvider),
            ("Invalid voice is rejected", InvalidVoice),
            ("Invalid rate and pitch are rejected", InvalidRateAndPitch),
            ("Invalid PCM output configuration is rejected", InvalidPcmConfiguration),
            ("Empty narration text is rejected", EmptyText),
            ("Overlong narration text is rejected", OverlongText),
            ("Running job can be cancelled", CancelRunningJob),
            ("Failed job requires explicit retry", FailedJobRequiresExplicitRetry),
            ("Succeeded job and candidate survive restart", SuccessSurvivesRestart),
            ("Failed job survives restart", FailureSurvivesRestart),
            ("Interrupted running job is recovered as failed", RunningRecovery),
            ("TTS failure does not change binding or published content", FailureDoesNotChangePublishedContent),
            ("Candidate does not auto-adopt or auto-publish", CandidateDoesNotAdoptOrPublish)
        };

        var failures = 0;
        foreach (var test in tests)
        {
            try
            {
                await test.Run();
                Console.WriteLine($"PASS  {test.Name}");
            }
            catch (Exception exception)
            {
                failures++;
                Console.Error.WriteLine($"FAIL  {test.Name}: {exception}");
            }
        }

        Console.WriteLine($"Phase 9B tests: {tests.Length - failures}/{tests.Length} passed.");
        return failures == 0 ? 0 : 1;
    }

    private static async Task DeterministicProviderIsStable()
    {
        var provider = new DeterministicTestTtsProvider();
        var configuration = Configuration with
        {
            ProviderKey = DeterministicTestTtsProvider.Id,
            Voice = DeterministicTestTtsProvider.VoiceId
        };
        var textFingerprint = NarrationAudioFingerprint.ComputeText("same input");
        var configurationFingerprint = NarrationAudioFingerprint.ComputeSynthesisConfiguration(configuration);
        var request = new TtsProviderSynthesisRequest("same input", textFingerprint, configuration,
            configurationFingerprint);
        var first = await provider.SynthesizeAsync(request, CancellationToken.None);
        var second = await provider.SynthesizeAsync(request, CancellationToken.None);
        await using (first.AudioStream)
        await using (second.AudioStream)
        {
            using var firstBuffer = new MemoryStream();
            using var secondBuffer = new MemoryStream();
            await first.AudioStream.CopyToAsync(firstBuffer);
            await second.AudioStream.CopyToAsync(secondBuffer);
            True(firstBuffer.ToArray().SequenceEqual(secondBuffer.ToArray()),
                "Deterministic provider returned different bytes for the same request.");
        }
    }

    private static async Task NormalGeneration()
    {
        var provider = ScriptedProvider.Success();
        await using var context = TestContext.Create(provider);
        var job = await CreateAndWaitAsync(context);
        Equal(TtsProductionJobStatus.Succeeded, job.Status);
        True(job.StartedAtUtc.HasValue && job.CompletedAtUtc.HasValue, "Job timestamps are incomplete.");
        Equal(1, job.Attempts.Count);
        True(job.Attempts[0].Succeeded, "The successful attempt was not recorded.");
        True(!string.IsNullOrWhiteSpace(job.CandidateId), "Candidate ID is missing.");
        var candidate = await context.Service.GetCandidateAsync(job.CandidateId!, CancellationToken.None);
        True(candidate is { Validation.Valid: true }, "Validated candidate was not persisted.");
    }

    private static async Task AssetIntegrity()
    {
        await using var context = TestContext.Create(ScriptedProvider.Success());
        var job = await CreateAndWaitAsync(context);
        var candidate = (await context.Service.GetCandidateAsync(job.CandidateId!, CancellationToken.None))!;
        var path = Path.Combine(context.AssetStorage.MediaDirectory, Path.GetFileName(candidate.Asset.Url));
        var bytes = await File.ReadAllBytesAsync(path);
        Equal(bytes.LongLength, candidate.Asset.SizeBytes);
        Equal(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), candidate.Asset.Sha256);
        Equal("audio/wav", candidate.Asset.MediaType);
        True(candidate.Asset.DurationSeconds > 0, "Duration was not extracted from WAV media.");
    }

    private static async Task SequentialIdempotency()
    {
        var provider = ScriptedProvider.Success();
        await using var context = TestContext.Create(provider);
        var first = await context.Service.CreateAsync(Request(), "tester", CancellationToken.None);
        var second = await context.Service.CreateAsync(Request(), "tester", CancellationToken.None);
        Equal(first.Job.JobId, second.Job.JobId);
        True(first.Created && !second.Created, "Sequential duplicate request created a second job.");
        await WaitForTerminalAsync(context.Service, first.Job.JobId);
        Equal(1, provider.CallCount);
    }

    private static async Task ConcurrentIdempotency()
    {
        var provider = ScriptedProvider.Success(TimeSpan.FromMilliseconds(75));
        await using var context = TestContext.Create(provider);
        var requests = Enumerable.Range(0, 20)
            .Select(_ => context.Service.CreateAsync(Request(), "tester", CancellationToken.None));
        var results = await Task.WhenAll(requests);
        Equal(1, results.Select(item => item.Job.JobId).Distinct(StringComparer.Ordinal).Count());
        Equal(1, results.Count(item => item.Created));
        await WaitForTerminalAsync(context.Service, results[0].Job.JobId);
        Equal(1, provider.CallCount);
    }

    private static async Task PermanentFailure()
    {
        var provider = ScriptedProvider.PermanentFailure();
        await using var context = TestContext.Create(provider);
        var job = await CreateAndWaitAsync(context);
        Equal(TtsProductionJobStatus.Failed, job.Status);
        Equal(TtsProductionErrorCategory.PermanentInput, job.ErrorCategory);
        Equal(1, provider.CallCount);
        Equal(1, job.Attempts.Count);
        True(job.CandidateId is null, "Failed job created a candidate.");
    }

    private static async Task TransientFailureHasFiniteRetries()
    {
        var provider = ScriptedProvider.TransientFailure();
        await using var context = TestContext.Create(provider, Options(maxAttempts: 3));
        var job = await CreateAndWaitAsync(context);
        Equal(TtsProductionJobStatus.Failed, job.Status);
        Equal(TtsProductionErrorCategory.TransientProvider, job.ErrorCategory);
        Equal(3, provider.CallCount);
        Equal(3, job.Attempts.Count);
        Equal(2, job.RetryCount);
    }

    private static async Task ProviderTimeout()
    {
        var provider = ScriptedProvider.Timeout();
        await using var context = TestContext.Create(provider, Options(maxAttempts: 2, timeoutMilliseconds: 30));
        var job = await CreateAndWaitAsync(context);
        Equal(TtsProductionJobStatus.Failed, job.Status);
        Equal("provider_timeout", job.ErrorCode);
        Equal(2, provider.CallCount);
        Equal(2, job.Attempts.Count);
    }

    private static async Task EmptyMedia()
    {
        await using var context = TestContext.Create(ScriptedProvider.Empty());
        var job = await CreateAndWaitAsync(context);
        Equal(TtsProductionErrorCategory.InvalidMedia, job.ErrorCategory);
        Equal("audio_too_small", job.ErrorCode);
        True(job.CandidateId is null, "Empty media created a candidate.");
    }

    private static async Task CorruptMedia()
    {
        await using var context = TestContext.Create(ScriptedProvider.Corrupt());
        var job = await CreateAndWaitAsync(context);
        Equal(TtsProductionErrorCategory.InvalidMedia, job.ErrorCategory);
        True(job.CandidateId is null, "Corrupt media created a candidate.");
    }

    private static async Task InvalidProvider()
    {
        await using var context = TestContext.Create(ScriptedProvider.Success());
        await ThrowsRequestAsync("invalid_provider", () => context.Service.CreateAsync(
            Request(Configuration with { ProviderKey = "missing" }), "tester", CancellationToken.None));
    }

    private static async Task InvalidVoice()
    {
        await using var context = TestContext.Create(ScriptedProvider.Success());
        await ThrowsRequestAsync("invalid_voice", () => context.Service.CreateAsync(
            Request(Configuration with { Voice = "missing" }), "tester", CancellationToken.None));
    }

    private static async Task InvalidRateAndPitch()
    {
        await using var context = TestContext.Create(ScriptedProvider.Success());
        await ThrowsRequestAsync("invalid_rate", () => context.Service.CreateAsync(
            Request(Configuration with { Rate = 3 }), "tester", CancellationToken.None));
        await ThrowsRequestAsync("invalid_pitch", () => context.Service.CreateAsync(
            Request(Configuration with { Pitch = 2 }), "tester", CancellationToken.None));
    }

    private static async Task InvalidPcmConfiguration()
    {
        await using var context = TestContext.Create(ScriptedProvider.Success());
        await ThrowsRequestAsync("invalid_volume", () => context.Service.CreateAsync(
            Request(Configuration with { Volume = 3 }), "tester", CancellationToken.None));
        await ThrowsRequestAsync("invalid_sample_rate", () => context.Service.CreateAsync(
            Request(Configuration with { SampleRateHz = 96000 }), "tester", CancellationToken.None));
        await ThrowsRequestAsync("invalid_channels", () => context.Service.CreateAsync(
            Request(Configuration with { Channels = 8 }), "tester", CancellationToken.None));
    }

    private static async Task EmptyText()
    {
        await using var context = TestContext.Create(ScriptedProvider.Success());
        await ThrowsRequestAsync("empty_text", () => context.Service.CreateAsync(
            Request() with { NarrationText = "  \r\n " }, "tester", CancellationToken.None));
    }

    private static async Task OverlongText()
    {
        await using var context = TestContext.Create(ScriptedProvider.Success(), Options(maxTextLength: 10));
        await ThrowsRequestAsync("text_too_long", () => context.Service.CreateAsync(
            Request() with { NarrationText = new string('a', 11) }, "tester", CancellationToken.None));
    }

    private static async Task CancelRunningJob()
    {
        var provider = ScriptedProvider.Timeout();
        await using var context = TestContext.Create(provider, Options(timeoutMilliseconds: 5000));
        var created = await context.Service.CreateAsync(Request(), "tester", CancellationToken.None);
        await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await context.Service.CancelAsync(created.Job.JobId, CancellationToken.None);
        var job = await WaitForTerminalAsync(context.Service, created.Job.JobId);
        Equal(TtsProductionJobStatus.Cancelled, job.Status);
        Equal(TtsProductionErrorCategory.Cancelled, job.ErrorCategory);
        True(job.CandidateId is null, "Cancelled job created a candidate.");
    }

    private static async Task FailedJobRequiresExplicitRetry()
    {
        var provider = ScriptedProvider.FailOnceThenSuccess();
        await using var context = TestContext.Create(provider);
        var failed = await CreateAndWaitAsync(context);
        Equal(TtsProductionJobStatus.Failed, failed.Status);
        var duplicate = await context.Service.CreateAsync(Request(), "tester", CancellationToken.None);
        Equal(failed.JobId, duplicate.Job.JobId);
        True(!duplicate.Created, "A failed job retried without explicit permission.");
        var retry = await context.Service.CreateAsync(Request() with { RetryFailed = true }, "tester", CancellationToken.None);
        True(retry.Created && retry.Job.JobId != failed.JobId, "Explicit retry did not create a new job.");
        Equal(TtsProductionJobStatus.Succeeded, (await WaitForTerminalAsync(context.Service, retry.Job.JobId)).Status);
        Equal(2, provider.CallCount);
    }

    private static async Task SuccessSurvivesRestart()
    {
        using var root = new TemporaryRoot();
        var first = TestContext.Create(ScriptedProvider.Success(), rootPath: root.Path, ownsRoot: false);
        var job = await CreateAndWaitAsync(first);
        var candidateId = job.CandidateId!;
        await first.DisposeAsync();

        await using var second = TestContext.Create(ScriptedProvider.Success(), rootPath: root.Path, ownsRoot: false);
        await second.Service.InitializeAsync(CancellationToken.None);
        Equal(TtsProductionJobStatus.Succeeded,
            (await second.Service.GetJobAsync(job.JobId, CancellationToken.None))!.Status);
        True(await second.Service.GetCandidateAsync(candidateId, CancellationToken.None) is not null,
            "Candidate disappeared after restart.");
    }

    private static async Task FailureSurvivesRestart()
    {
        using var root = new TemporaryRoot();
        var first = TestContext.Create(ScriptedProvider.PermanentFailure(), rootPath: root.Path, ownsRoot: false);
        var job = await CreateAndWaitAsync(first);
        await first.DisposeAsync();
        await using var second = TestContext.Create(ScriptedProvider.Success(), rootPath: root.Path, ownsRoot: false);
        var recovered = await second.Service.GetJobAsync(job.JobId, CancellationToken.None);
        Equal(TtsProductionJobStatus.Failed, recovered!.Status);
        Equal("invalid_input", recovered.ErrorCode);
    }

    private static async Task RunningRecovery()
    {
        using var root = new TemporaryRoot();
        var environment = new TestEnvironment(root.Path);
        var storageOptions = Microsoft.Extensions.Options.Options.Create(new StorageOptions { DataDirectory = "Data" });
        var repository = new TtsProductionRepository(storageOptions, environment);
        await repository.InitializeAsync(CancellationToken.None);
        var now = DateTimeOffset.UtcNow;
        var job = NewQueuedJob(now);
        await repository.CreateOrGetAsync(job, false, CancellationToken.None);
        await repository.UpdateJobAsync(job.JobId,
            current => current with { Status = TtsProductionJobStatus.Running, StartedAtUtc = now }, CancellationToken.None);

        var restarted = new TtsProductionRepository(storageOptions, environment);
        await restarted.InitializeAsync(CancellationToken.None);
        var recovered = await restarted.GetJobAsync(job.JobId, CancellationToken.None);
        Equal(TtsProductionJobStatus.Failed, recovered!.Status);
        Equal(TtsProductionErrorCategory.Interrupted, recovered.ErrorCategory);
        Equal("server_interrupted", recovered.ErrorCode);
        Equal(1, recovered.Attempts.Count);
    }

    private static async Task FailureDoesNotChangePublishedContent()
    {
        await using var context = TestContext.Create(ScriptedProvider.PermanentFailure());
        var before = await SeedPublishedContentAsync(context, "Audio A");
        var job = await CreateAndWaitAsync(context, Request() with { NarrationText = "A new failed narration" });
        Equal(TtsProductionJobStatus.Failed, job.Status);
        var after = await context.ContentRepository.GetAsync(CancellationToken.None);
        Equal(before.Version, after.Version);
        Equal(before.Modules[0].Nodes[0].NarrationAudio, after.Modules[0].Nodes[0].NarrationAudio);
    }

    private static async Task CandidateDoesNotAdoptOrPublish()
    {
        await using var context = TestContext.Create(ScriptedProvider.Success());
        var before = await SeedPublishedContentAsync(context, "Audio A");
        var job = await CreateAndWaitAsync(context, Request() with { NarrationText = "Candidate B" });
        Equal(TtsProductionJobStatus.Succeeded, job.Status);
        var after = await context.ContentRepository.GetAsync(CancellationToken.None);
        Equal(before.Version, after.Version);
        Equal(before.Modules[0].Nodes[0].NarrationAudio, after.Modules[0].Nodes[0].NarrationAudio);
        True(job.CandidateId is not null, "Candidate was not created independently.");
    }

    private static async Task<PublishedContent> SeedPublishedContentAsync(TestContext context, string text)
    {
        var bytes = CreateWave();
        await using var source = new MemoryStream(bytes);
        var asset = await context.AssetStorage.ImportValidatedAudioAsync(source, "existing.wav", "audio/wav", 0.25,
            CancellationToken.None);
        var binding = new NarrationAudioBinding(asset, NarrationAudioFingerprint.ComputeText(text),
            NarrationAudioFingerprint.ComputeSynthesisConfiguration(Configuration), Configuration,
            NarrationAudioOrigin.Generated, DateTimeOffset.UtcNow, NarrationAudioFingerprint.Version, "existing");
        var node = new NarrationNode("node", "Node", 1, text, asset.Url, [], TtsConfiguration: Configuration,
            NarrationAudio: binding);
        var module = new ExhibitionModule("module", "Module", 1, string.Empty, null, true, [node]);
        return await context.ContentRepository.SaveAsync([module], "tester", CancellationToken.None);
    }

    private static async Task<TtsProductionJob> CreateAndWaitAsync(TestContext context,
        CreateTtsProductionJobRequest? request = null)
    {
        var result = await context.Service.CreateAsync(request ?? Request(), "tester", CancellationToken.None);
        return await WaitForTerminalAsync(context.Service, result.Job.JobId);
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

    private static async Task ThrowsRequestAsync(string expectedCode, Func<Task> action)
    {
        try { await action(); }
        catch (TtsProductionRequestException exception)
        {
            Equal(expectedCode, exception.ErrorCode);
            return;
        }
        throw new InvalidOperationException($"Expected request error '{expectedCode}'.");
    }

    private static CreateTtsProductionJobRequest Request(TtsSynthesisConfiguration? configuration = null) =>
        new("module", "node", "智慧展厅自动讲解测试文案。", configuration ?? Configuration);

    private static TtsProductionOptions Options(int maxAttempts = 3, int timeoutMilliseconds = 500,
        int maxTextLength = 5000) => new()
    {
        MaxAttempts = maxAttempts,
        AttemptTimeoutMilliseconds = timeoutMilliseconds,
        RetryDelayMilliseconds = 1,
        MaxTextLength = maxTextLength,
        MinAudioSizeBytes = 45,
        MaxAudioSizeBytes = 1024 * 1024
    };

    private static TtsProductionJob NewQueuedJob(DateTimeOffset now)
    {
        var textFingerprint = NarrationAudioFingerprint.ComputeText("interrupted");
        var configurationFingerprint = NarrationAudioFingerprint.ComputeSynthesisConfiguration(Configuration);
        return new TtsProductionJob(Guid.NewGuid().ToString("N"), "module", "node", "tester", "interrupted",
            textFingerprint, Configuration, configurationFingerprint, Guid.NewGuid().ToString("N"), "test", "voice",
            TtsProductionJobStatus.Queued, now, null, null, 0, []);
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

    private sealed class ScriptedProvider : ITtsProvider
    {
        private readonly Func<CancellationToken, Task<TtsProviderAudioResult>> synthesize;
        private int callCount;

        private ScriptedProvider(Func<CancellationToken, Task<TtsProviderAudioResult>> synthesize) =>
            this.synthesize = synthesize;

        public string ProviderId => "test";
        public int CallCount => callCount;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<TtsProviderDescriptor> GetDescriptorAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new TtsProviderDescriptor(ProviderId, "Test provider", true, true, null,
                [new TtsVoiceDescriptor("voice", "Voice", "zh-CN")],
                new TtsProviderCapabilities(5000, 0.5, 2, -1, 1, ["audio/wav"])));

        public async Task<TtsProviderAudioResult> SynthesizeAsync(TtsProviderSynthesisRequest request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref callCount);
            Started.TrySetResult();
            return await synthesize(cancellationToken);
        }

        public static ScriptedProvider Success(TimeSpan? delay = null) => new(async cancellationToken =>
        {
            if (delay.HasValue) await Task.Delay(delay.Value, cancellationToken);
            return new TtsProviderAudioResult(new MemoryStream(CreateWave(), false), "audio/wav", "test-request");
        });

        public static ScriptedProvider PermanentFailure() => new(_ =>
            throw new TtsProviderException(TtsProviderFailureKind.Permanent, "invalid_input", "Invalid input."));

        public static ScriptedProvider TransientFailure() => new(_ =>
            throw new TtsProviderException(TtsProviderFailureKind.Transient, "temporary", "Temporary failure."));

        public static ScriptedProvider FailOnceThenSuccess()
        {
            var calls = 0;
            return new ScriptedProvider(_ => Interlocked.Increment(ref calls) == 1
                ? Task.FromException<TtsProviderAudioResult>(new TtsProviderException(
                    TtsProviderFailureKind.Permanent, "invalid_input", "Invalid input."))
                : Task.FromResult(new TtsProviderAudioResult(new MemoryStream(CreateWave(), false), "audio/wav")));
        }

        public static ScriptedProvider Timeout() => new(async cancellationToken =>
        {
            await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Unreachable");
        });

        public static ScriptedProvider Empty() => new(_ =>
            Task.FromResult(new TtsProviderAudioResult(new MemoryStream(), "audio/wav")));

        public static ScriptedProvider Corrupt() => new(_ =>
            Task.FromResult(new TtsProviderAudioResult(new MemoryStream(new byte[100]), "audio/wav")));
    }

    private sealed class TestContext : IAsyncDisposable
    {
        private readonly bool ownsRoot;

        private TestContext(string rootPath, bool ownsRoot, TtsProductionService service, AssetStorage assetStorage,
            JsonContentRepository contentRepository)
        {
            RootPath = rootPath;
            this.ownsRoot = ownsRoot;
            Service = service;
            AssetStorage = assetStorage;
            ContentRepository = contentRepository;
        }

        public string RootPath { get; }
        public TtsProductionService Service { get; }
        public AssetStorage AssetStorage { get; }
        public JsonContentRepository ContentRepository { get; }

        public static TestContext Create(ITtsProvider provider, TtsProductionOptions? productionOptions = null,
            string? rootPath = null, bool ownsRoot = true)
        {
            rootPath ??= Path.Combine(Path.GetTempPath(), "tg-phase9b-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(rootPath);
            var environment = new TestEnvironment(rootPath);
            var storageOptions = Microsoft.Extensions.Options.Options.Create(new StorageOptions { DataDirectory = "Data" });
            var configuredProduction = Microsoft.Extensions.Options.Options.Create(productionOptions ?? Options());
            var assetStorage = new AssetStorage(storageOptions, environment);
            var repository = new TtsProductionRepository(storageOptions, environment);
            var validator = new TtsMediaValidator(configuredProduction, storageOptions, environment);
            var service = new TtsProductionService(new TtsProviderRegistry([provider]), repository, validator,
                assetStorage, configuredProduction, NullLogger<TtsProductionService>.Instance);
            var contentRepository = new JsonContentRepository(storageOptions, environment);
            return new TestContext(rootPath, ownsRoot, service, assetStorage, contentRepository);
        }

        public async ValueTask DisposeAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await Service.StopAsync(timeout.Token);
            if (ownsRoot && Directory.Exists(RootPath)) Directory.Delete(RootPath, true);
        }
    }

    private sealed class TemporaryRoot : IDisposable
    {
        public TemporaryRoot()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tg-phase9b-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }
        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, true);
        }
    }

    private sealed class TestEnvironment(string contentRootPath) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "TG.Control.Phase9B.Tests";
        public string ContentRootPath { get; set; } = contentRootPath;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
