using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using TG.Control.Contracts;

namespace TG.Control.Server;

public sealed class TtsProductionRequestException(string errorCode, string message) : ArgumentException(message)
{
    public string ErrorCode { get; } = errorCode;
}

public sealed class TtsProductionService(
    TtsProviderRegistry providers,
    TtsProductionRepository repository,
    TtsMediaValidator mediaValidator,
    AssetStorage assetStorage,
    IOptions<TtsProductionOptions> options,
    ILogger<TtsProductionService> logger) : IHostedService
{
    private readonly ConcurrentDictionary<string, Task> runningTasks = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, CancellationTokenSource> jobCancellation = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim initializationGate = new(1, 1);
    private readonly CancellationTokenSource serviceCancellation = new();
    private bool initialized;

    public async Task StartAsync(CancellationToken cancellationToken) => await InitializeAsync(cancellationToken);

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        serviceCancellation.Cancel();
        foreach (var cancellation in jobCancellation.Values) cancellation.Cancel();
        var tasks = runningTasks.Values.ToArray();
        if (tasks.Length > 0) await Task.WhenAll(tasks).WaitAsync(cancellationToken);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (initialized) return;
        await initializationGate.WaitAsync(cancellationToken);
        try
        {
            if (initialized) return;
            var queued = await repository.InitializeAsync(cancellationToken);
            initialized = true;
            foreach (var job in queued) Schedule(job.JobId);
        }
        finally { initializationGate.Release(); }
    }

    public Task<IReadOnlyList<TtsProviderDescriptor>> GetProvidersAsync(CancellationToken cancellationToken) =>
        providers.GetDescriptorsAsync(cancellationToken);

    public async Task<CreateTtsProductionJobResponse> CreateAsync(CreateTtsProductionJobRequest request, string requestedBy,
        CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);
        var validated = await ValidateRequestAsync(request, cancellationToken);
        var textFingerprint = NarrationAudioFingerprint.ComputeText(validated.NarrationText);
        var configurationFingerprint = NarrationAudioFingerprint.ComputeSynthesisConfiguration(validated.SynthesisConfiguration);
        var idempotencyKey = ComputeIdempotencyKey(textFingerprint, configurationFingerprint);
        var now = DateTimeOffset.UtcNow;
        var job = new TtsProductionJob(Guid.NewGuid().ToString("N"), validated.ModuleId.Trim(), validated.NodeId.Trim(),
            string.IsNullOrWhiteSpace(requestedBy) ? "unknown" : requestedBy.Trim(), validated.NarrationText,
            textFingerprint, validated.SynthesisConfiguration, configurationFingerprint, idempotencyKey,
            validated.SynthesisConfiguration.ProviderKey.Trim(), validated.SynthesisConfiguration.Voice.Trim(),
            TtsProductionJobStatus.Queued, now, null, null, 0, []);

        var result = await repository.CreateOrGetAsync(job, request.RetryFailed, cancellationToken);
        if (result.Created) Schedule(result.Job.JobId);
        return result;
    }

    public async Task<TtsProductionJob?> GetJobAsync(string jobId, CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);
        return await repository.GetJobAsync(jobId, cancellationToken);
    }

    public async Task<NarrationAudioCandidate?> GetCandidateAsync(string candidateId, CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);
        return await repository.GetCandidateAsync(candidateId, cancellationToken);
    }

    public async Task<TtsProductionJob?> CancelAsync(string jobId, CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);
        var job = await repository.GetJobAsync(jobId, cancellationToken);
        if (job is null || job.Status is TtsProductionJobStatus.Succeeded or TtsProductionJobStatus.Failed or
            TtsProductionJobStatus.Cancelled) return job;

        if (job.Status == TtsProductionJobStatus.Queued)
        {
            job = await repository.UpdateJobAsync(jobId, current => current.Status == TtsProductionJobStatus.Queued
                ? current with
                {
                    Status = TtsProductionJobStatus.Cancelled,
                    CompletedAtUtc = DateTimeOffset.UtcNow,
                    ErrorCategory = TtsProductionErrorCategory.Cancelled,
                    ErrorCode = "cancelled",
                    ErrorMessage = "TTS production was cancelled."
                }
                : current, cancellationToken);
        }

        if (jobCancellation.TryGetValue(jobId, out var source)) source.Cancel();
        else if (job.Status == TtsProductionJobStatus.Running)
            job = await MarkCancelledAsync(jobId);
        return job;
    }

    private void Schedule(string jobId)
    {
        if (serviceCancellation.IsCancellationRequested) return;
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(serviceCancellation.Token);
        if (!jobCancellation.TryAdd(jobId, cancellation))
        {
            cancellation.Dispose();
            return;
        }

        var task = Task.Run(() => ProcessAsync(jobId, cancellation.Token), CancellationToken.None);
        if (!runningTasks.TryAdd(jobId, task))
        {
            jobCancellation.TryRemove(jobId, out _);
            cancellation.Dispose();
            return;
        }

        _ = task.ContinueWith(completedTask =>
        {
            _ = completedTask.Exception;
            runningTasks.TryRemove(jobId, out Task? ignoredTask);
            if (jobCancellation.TryRemove(jobId, out var completedCancellation)) completedCancellation.Dispose();
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private async Task ProcessAsync(string jobId, CancellationToken jobToken)
    {
        var job = await repository.GetJobAsync(jobId, CancellationToken.None);
        if (job is null || job.Status != TtsProductionJobStatus.Queued) return;
        job = await repository.UpdateJobAsync(jobId, current => current.Status == TtsProductionJobStatus.Queued
            ? current with { Status = TtsProductionJobStatus.Running, StartedAtUtc = DateTimeOffset.UtcNow }
            : current, CancellationToken.None);
        if (job.Status != TtsProductionJobStatus.Running) return;

        if (!providers.TryResolve(job.ProviderId, out var provider))
        {
            await MarkFailedAsync(job, TtsProductionErrorCategory.PermanentInput, "provider_unavailable",
                "The configured TTS provider is no longer available.", null);
            return;
        }

        var maxAttempts = Math.Clamp(options.Value.MaxAttempts, 1, 10);
        for (var attemptNumber = 1; attemptNumber <= maxAttempts; attemptNumber++)
        {
            var started = DateTimeOffset.UtcNow;
            try
            {
                jobToken.ThrowIfCancellationRequested();
                using var attemptCancellation = CancellationTokenSource.CreateLinkedTokenSource(jobToken);
                attemptCancellation.CancelAfter(TimeSpan.FromMilliseconds(Math.Max(10, options.Value.AttemptTimeoutMilliseconds)));
                var result = await provider.SynthesizeAsync(new TtsProviderSynthesisRequest(job.NarrationText,
                    job.NarrationTextFingerprint, job.SynthesisConfiguration,
                    job.SynthesisConfigurationFingerprint), attemptCancellation.Token);
                if (result is null || result.AudioStream is null)
                    throw new TtsMediaValidationException("empty_result", "The provider returned no audio data.");

                await using (result.AudioStream)
                {
                    jobToken.ThrowIfCancellationRequested();
                    var validated = await mediaValidator.ValidateAsync(result.AudioStream, result.MediaType,
                        job.SynthesisConfiguration, attemptCancellation.Token);
                    try
                    {
                        jobToken.ThrowIfCancellationRequested();
                        await using var source = File.OpenRead(validated.FilePath);
                        var asset = await assetStorage.ImportValidatedAudioAsync(source, $"tts-{job.JobId}.wav",
                            validated.MediaType, validated.DurationSeconds, jobToken);
                        jobToken.ThrowIfCancellationRequested();
                        var now = DateTimeOffset.UtcNow;
                        var attempts = job.Attempts.Append(new TtsProductionJobAttempt(attemptNumber, started, now, true)).ToArray();
                        var candidate = new NarrationAudioCandidate(Guid.NewGuid().ToString("N"), job.JobId, asset,
                            job.NarrationTextFingerprint, job.SynthesisConfigurationFingerprint,
                            job.SynthesisConfiguration, job.ProviderId, job.Voice, now,
                            new NarrationAudioCandidateValidation(true, "pcm-wave-v1", validated.MediaType,
                                validated.DurationSeconds, now), result.ProviderRequestId);
                        job = job with
                        {
                            Status = TtsProductionJobStatus.Succeeded,
                            CompletedAtUtc = now,
                            RetryCount = Math.Max(0, attemptNumber - 1),
                            Attempts = attempts,
                            ErrorCategory = null,
                            ErrorCode = null,
                            ErrorMessage = null,
                            CandidateId = candidate.CandidateId
                        };
                        await repository.CompleteAsync(job, candidate, CancellationToken.None);
                        return;
                    }
                    finally
                    {
                        try
                        {
                            if (File.Exists(validated.FilePath)) File.Delete(validated.FilePath);
                        }
                        catch (IOException exception)
                        {
                            logger.LogWarning(exception, "Could not delete TTS staging file {FilePath}.", validated.FilePath);
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (jobToken.IsCancellationRequested)
            {
                await MarkCancelledAsync(jobId, started, attemptNumber);
                return;
            }
            catch (OperationCanceledException)
            {
                var shouldRetry = attemptNumber < maxAttempts;
                job = await RecordFailureAsync(job, attemptNumber, started, TtsProductionErrorCategory.TransientProvider,
                    "provider_timeout", "The TTS provider attempt timed out.", shouldRetry);
                if (!shouldRetry) return;
            }
            catch (TtsProviderException exception)
            {
                var category = exception.FailureKind == TtsProviderFailureKind.Transient
                    ? TtsProductionErrorCategory.TransientProvider
                    : TtsProductionErrorCategory.PermanentInput;
                var shouldRetry = exception.FailureKind == TtsProviderFailureKind.Transient && attemptNumber < maxAttempts;
                job = await RecordFailureAsync(job, attemptNumber, started, category, exception.ErrorCode,
                    exception.Message, shouldRetry);
                if (!shouldRetry) return;
            }
            catch (TtsMediaValidationException exception)
            {
                await RecordFailureAsync(job, attemptNumber, started, TtsProductionErrorCategory.InvalidMedia,
                    exception.ErrorCode, exception.Message, false);
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "TTS job {JobId} failed unexpectedly.", jobId);
                await RecordFailureAsync(job, attemptNumber, started, TtsProductionErrorCategory.Internal,
                    "internal_error", "The server could not complete TTS production.", false);
                return;
            }

            if (options.Value.RetryDelayMilliseconds > 0)
            {
                try { await Task.Delay(options.Value.RetryDelayMilliseconds, jobToken); }
                catch (OperationCanceledException)
                {
                    await MarkCancelledAsync(jobId);
                    return;
                }
            }
        }
    }

    private async Task<TtsProductionJob> RecordFailureAsync(TtsProductionJob job, int attemptNumber,
        DateTimeOffset started, TtsProductionErrorCategory category, string code, string message, bool retry)
    {
        var now = DateTimeOffset.UtcNow;
        var attempt = new TtsProductionJobAttempt(attemptNumber, started, now, false, category, code, message);
        return await repository.UpdateJobAsync(job.JobId, current => current with
        {
            Status = retry ? TtsProductionJobStatus.Running : TtsProductionJobStatus.Failed,
            CompletedAtUtc = retry ? null : now,
            RetryCount = Math.Max(0, attemptNumber - 1),
            Attempts = current.Attempts.Append(attempt).ToArray(),
            ErrorCategory = retry ? null : category,
            ErrorCode = retry ? null : code,
            ErrorMessage = retry ? null : message
        }, CancellationToken.None);
    }

    private async Task MarkFailedAsync(TtsProductionJob job, TtsProductionErrorCategory category, string code,
        string message, TtsProductionJobAttempt? attempt)
    {
        await repository.UpdateJobAsync(job.JobId, current => current with
        {
            Status = TtsProductionJobStatus.Failed,
            CompletedAtUtc = DateTimeOffset.UtcNow,
            Attempts = attempt is null ? current.Attempts : current.Attempts.Append(attempt).ToArray(),
            ErrorCategory = category,
            ErrorCode = code,
            ErrorMessage = message
        }, CancellationToken.None);
    }

    private Task<TtsProductionJob> MarkCancelledAsync(string jobId, DateTimeOffset? started = null,
        int? attemptNumber = null) => repository.UpdateJobAsync(jobId, current =>
    {
        if (current.Status is TtsProductionJobStatus.Succeeded or TtsProductionJobStatus.Failed or
            TtsProductionJobStatus.Cancelled) return current;
        var now = DateTimeOffset.UtcNow;
        var attempts = current.Attempts;
        if (started.HasValue && attemptNumber.HasValue)
            attempts = attempts.Append(new TtsProductionJobAttempt(attemptNumber.Value, started.Value, now, false,
                TtsProductionErrorCategory.Cancelled, "cancelled", "TTS production was cancelled.")).ToArray();
        return current with
        {
            Status = TtsProductionJobStatus.Cancelled,
            CompletedAtUtc = now,
            Attempts = attempts,
            RetryCount = Math.Max(0, attempts.Count - 1),
            ErrorCategory = TtsProductionErrorCategory.Cancelled,
            ErrorCode = "cancelled",
            ErrorMessage = "TTS production was cancelled."
        };
    }, CancellationToken.None);

    private async Task<CreateTtsProductionJobRequest> ValidateRequestAsync(CreateTtsProductionJobRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.ModuleId)) throw InvalidRequest("missing_module", "Module ID is required.");
        if (string.IsNullOrWhiteSpace(request.NodeId)) throw InvalidRequest("missing_node", "Node ID is required.");
        var text = NarrationAudioFingerprint.NormalizeText(request.NarrationText);
        if (string.IsNullOrWhiteSpace(text)) throw InvalidRequest("empty_text", "Narration text cannot be empty.");
        if (text.Length > Math.Max(1, options.Value.MaxTextLength))
            throw InvalidRequest("text_too_long", $"Narration text exceeds {options.Value.MaxTextLength} characters.");
        if (!NarrationAudioFingerprint.IsSynthesisConfigurationValid(request.SynthesisConfiguration))
            throw InvalidRequest("invalid_configuration", "TTS synthesis configuration is invalid.");
        if (!providers.TryResolve(request.SynthesisConfiguration.ProviderKey, out var provider))
            throw InvalidRequest("invalid_provider", "The requested TTS provider does not exist.");

        var descriptor = await provider.GetDescriptorAsync(cancellationToken);
        if (!descriptor.Available) throw InvalidRequest("provider_unavailable",
            descriptor.UnavailableReason ?? "The requested TTS provider is unavailable.");
        if (descriptor.Voices.All(item => !string.Equals(item.VoiceId, request.SynthesisConfiguration.Voice,
                StringComparison.OrdinalIgnoreCase)))
            throw InvalidRequest("invalid_voice", "The requested voice is not available from this provider.");
        var maxTextLength = Math.Min(Math.Max(1, options.Value.MaxTextLength), Math.Max(1, descriptor.Capabilities.MaxTextLength));
        if (text.Length > maxTextLength) throw InvalidRequest("text_too_long", $"Narration text exceeds {maxTextLength} characters.");
        if (request.SynthesisConfiguration.Rate < descriptor.Capabilities.MinRate ||
            request.SynthesisConfiguration.Rate > descriptor.Capabilities.MaxRate)
            throw InvalidRequest("invalid_rate", "Speech rate is outside the provider capability range.");
        if (!descriptor.Capabilities.SupportsRate && request.SynthesisConfiguration.Rate != 1)
            throw InvalidRequest("invalid_rate", "The provider does not support changing speech rate.");
        if (request.SynthesisConfiguration.Pitch < descriptor.Capabilities.MinPitch ||
            request.SynthesisConfiguration.Pitch > descriptor.Capabilities.MaxPitch)
            throw InvalidRequest("invalid_pitch", "Pitch is outside the provider capability range.");
        if (!descriptor.Capabilities.SupportsPitch && request.SynthesisConfiguration.Pitch != 0)
            throw InvalidRequest("invalid_pitch", "The provider does not support changing pitch.");
        if (request.SynthesisConfiguration.Volume > 2)
            throw InvalidRequest("invalid_volume", "Volume must be between 0 and 2.");
        if (!descriptor.Capabilities.SupportsVolume && request.SynthesisConfiguration.Volume != 1)
            throw InvalidRequest("invalid_volume", "The provider does not support changing synthesis volume.");
        if (request.SynthesisConfiguration.SampleRateHz != 0 &&
            request.SynthesisConfiguration.SampleRateHz is < 8000 or > 48000)
            throw InvalidRequest("invalid_sample_rate", "PCM sample rate must be 8-48 kHz or zero for provider default.");
        if (descriptor.Capabilities.FixedSampleRateHz > 0 && request.SynthesisConfiguration.SampleRateHz != 0 &&
            request.SynthesisConfiguration.SampleRateHz != descriptor.Capabilities.FixedSampleRateHz)
            throw InvalidRequest("invalid_sample_rate", "Requested sample rate is not supported by the provider.");
        if (request.SynthesisConfiguration.Channels is not (0 or 1 or 2))
            throw InvalidRequest("invalid_channels", "PCM channel count must be mono, stereo, or zero for provider default.");
        if (descriptor.Capabilities.FixedChannels > 0 && request.SynthesisConfiguration.Channels != 0 &&
            request.SynthesisConfiguration.Channels != descriptor.Capabilities.FixedChannels)
            throw InvalidRequest("invalid_channels", "Requested channel count is not supported by the provider.");
        if (!string.Equals(request.SynthesisConfiguration.OutputMediaType, "audio/wav", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(request.SynthesisConfiguration.OutputMediaType, "audio/x-wav", StringComparison.OrdinalIgnoreCase))
            throw InvalidRequest("invalid_media_type", "Phase 9B production accepts PCM WAV output only.");
        if (descriptor.Capabilities.SupportedMediaTypes.All(item =>
                !string.Equals(item, request.SynthesisConfiguration.OutputMediaType, StringComparison.OrdinalIgnoreCase)))
            throw InvalidRequest("invalid_media_type", "Requested output media type is not supported by the provider.");
        return request with { NarrationText = text };
    }

    private static string ComputeIdempotencyKey(string textFingerprint, string configurationFingerprint) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            "tg:tts-production-idempotency:v1\n" + textFingerprint + "\n" + configurationFingerprint)))
            .ToLowerInvariant();

    private static TtsProductionRequestException InvalidRequest(string code, string message) => new(code, message);
}
