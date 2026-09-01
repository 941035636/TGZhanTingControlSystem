using System.Text.Json;
using Microsoft.Extensions.Options;
using TG.Control.Contracts;

namespace TG.Control.Server;

public sealed class TtsProductionRepository
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly string filePath;
    private readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private TtsProductionState state = new();
    private bool initialized;

    public TtsProductionRepository(IOptions<StorageOptions> options, IHostEnvironment environment)
    {
        var directory = Path.GetFullPath(options.Value.DataDirectory, environment.ContentRootPath);
        Directory.CreateDirectory(directory);
        filePath = Path.Combine(directory, "tts-production.json");
    }

    public async Task<IReadOnlyList<TtsProductionJob>> InitializeAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (!initialized)
            {
                if (File.Exists(filePath))
                {
                    await using var stream = File.OpenRead(filePath);
                    state = await JsonSerializer.DeserializeAsync<TtsProductionState>(stream, jsonOptions, cancellationToken)
                            ?? throw new InvalidDataException("TTS production state is empty or invalid.");
                }

                var now = DateTimeOffset.UtcNow;
                var changed = false;
                for (var index = 0; index < state.Jobs.Count; index++)
                {
                    var job = state.Jobs[index];
                    if (job.Status != TtsProductionJobStatus.Running) continue;
                    var attempt = new TtsProductionJobAttempt(job.Attempts.Count + 1, job.StartedAtUtc ?? job.CreatedAtUtc,
                        now, false, TtsProductionErrorCategory.Interrupted, "server_interrupted",
                        "Server stopped while the synthesis attempt was running.");
                    state.Jobs[index] = job with
                    {
                        Status = TtsProductionJobStatus.Failed,
                        CompletedAtUtc = now,
                        RetryCount = Math.Max(0, job.Attempts.Count),
                        Attempts = job.Attempts.Append(attempt).ToArray(),
                        ErrorCategory = TtsProductionErrorCategory.Interrupted,
                        ErrorCode = "server_interrupted",
                        ErrorMessage = "Server stopped while the synthesis attempt was running."
                    };
                    changed = true;
                }
                initialized = true;
                if (changed) await WriteAsync(cancellationToken);
            }

            return state.Jobs.Where(item => item.Status == TtsProductionJobStatus.Queued).ToArray();
        }
        finally { gate.Release(); }
    }

    public async Task<CreateTtsProductionJobResponse> CreateOrGetAsync(TtsProductionJob proposed, bool retryFailed,
        CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var existing = state.Jobs.Where(item => string.Equals(item.IdempotencyKey, proposed.IdempotencyKey,
                    StringComparison.Ordinal))
                .OrderByDescending(item => item.CreatedAtUtc)
                .FirstOrDefault(item => item.Status is TtsProductionJobStatus.Queued or TtsProductionJobStatus.Running or
                    TtsProductionJobStatus.Succeeded);
            existing ??= !retryFailed
                ? state.Jobs.Where(item => string.Equals(item.IdempotencyKey, proposed.IdempotencyKey, StringComparison.Ordinal))
                    .OrderByDescending(item => item.CreatedAtUtc).FirstOrDefault()
                : null;
            if (existing is not null) return new CreateTtsProductionJobResponse(existing, false);

            state.Jobs.Add(proposed);
            await WriteAsync(cancellationToken);
            return new CreateTtsProductionJobResponse(proposed, true);
        }
        finally { gate.Release(); }
    }

    public async Task<TtsProductionJob?> GetJobAsync(string jobId, CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);
        await gate.WaitAsync(cancellationToken);
        try { return state.Jobs.FirstOrDefault(item => string.Equals(item.JobId, jobId, StringComparison.Ordinal)); }
        finally { gate.Release(); }
    }

    public async Task<NarrationAudioCandidate?> GetCandidateAsync(string candidateId, CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);
        await gate.WaitAsync(cancellationToken);
        try { return state.Candidates.FirstOrDefault(item => string.Equals(item.CandidateId, candidateId, StringComparison.Ordinal)); }
        finally { gate.Release(); }
    }

    public async Task<IReadOnlyList<NarrationAudioCandidate>> GetCandidatesAsync(CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);
        await gate.WaitAsync(cancellationToken);
        try { return state.Candidates.ToArray(); }
        finally { gate.Release(); }
    }

    public async Task<TtsProductionJob> UpdateJobAsync(string jobId, Func<TtsProductionJob, TtsProductionJob> update,
        CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var index = state.Jobs.FindIndex(item => string.Equals(item.JobId, jobId, StringComparison.Ordinal));
            if (index < 0) throw new KeyNotFoundException($"TTS job '{jobId}' does not exist.");
            state.Jobs[index] = update(state.Jobs[index]);
            await WriteAsync(cancellationToken);
            return state.Jobs[index];
        }
        finally { gate.Release(); }
    }

    public async Task CompleteAsync(TtsProductionJob job, NarrationAudioCandidate candidate,
        CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var index = state.Jobs.FindIndex(item => string.Equals(item.JobId, job.JobId, StringComparison.Ordinal));
            if (index < 0) throw new KeyNotFoundException($"TTS job '{job.JobId}' does not exist.");
            if (state.Candidates.All(item => !string.Equals(item.CandidateId, candidate.CandidateId, StringComparison.Ordinal)))
                state.Candidates.Add(candidate);
            state.Jobs[index] = job;
            await WriteAsync(cancellationToken);
        }
        finally { gate.Release(); }
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (!initialized) await InitializeAsync(cancellationToken);
    }

    private async Task WriteAsync(CancellationToken cancellationToken)
    {
        var tempPath = filePath + ".tmp";
        await using (var stream = File.Create(tempPath))
            await JsonSerializer.SerializeAsync(stream, state, jsonOptions, cancellationToken);
        File.Move(tempPath, filePath, true);
    }

    private sealed class TtsProductionState
    {
        public List<TtsProductionJob> Jobs { get; init; } = [];
        public List<NarrationAudioCandidate> Candidates { get; init; } = [];
    }
}
