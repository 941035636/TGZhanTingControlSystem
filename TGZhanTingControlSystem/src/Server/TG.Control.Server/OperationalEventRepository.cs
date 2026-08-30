using System.Text.Json;
using Microsoft.Extensions.Options;
using TG.Control.Contracts;

namespace TG.Control.Server;

public sealed class OperationalEventRepository
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly string filePath;
    private readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web);

    public OperationalEventRepository(IOptions<StorageOptions> options, IHostEnvironment environment)
    {
        var directory = Path.GetFullPath(options.Value.DataDirectory, environment.ContentRootPath);
        Directory.CreateDirectory(directory);
        filePath = Path.Combine(directory, "operational-events.jsonl");
    }

    public async Task<OperationalEvent> AppendAsync(string level, string category, string action, string message,
        string? sessionId = null, string? detail = null, string? clientId = null, string? nodeId = null,
        CancellationToken cancellationToken = default)
    {
        var item = new OperationalEvent(Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow,
            level, category, action, message, sessionId, detail, clientId, nodeId);
        var line = JsonSerializer.Serialize(item, jsonOptions) + Environment.NewLine;
        await gate.WaitAsync(cancellationToken);
        try { await File.AppendAllTextAsync(filePath, line, cancellationToken); }
        finally { gate.Release(); }
        return item;
    }

    public async Task<IReadOnlyList<OperationalEvent>> GetRecentAsync(int count, CancellationToken cancellationToken)
    {
        count = Math.Clamp(count, 1, 1000);
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(filePath)) return Array.Empty<OperationalEvent>();
            var queue = new Queue<OperationalEvent>(count);
            foreach (var line in File.ReadLines(filePath))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    var item = JsonSerializer.Deserialize<OperationalEvent>(line, jsonOptions);
                    if (item is null) continue;
                    if (queue.Count == count) queue.Dequeue();
                    queue.Enqueue(item);
                }
                catch (JsonException) { }
            }
            return queue.Reverse().ToArray();
        }
        finally { gate.Release(); }
    }
}
