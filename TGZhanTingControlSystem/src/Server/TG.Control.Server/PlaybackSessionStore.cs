using System.Text.Json;
using Microsoft.Extensions.Options;

namespace TG.Control.Server;

public sealed record PlaybackNodeSnapshot(string ModuleId, string NodeId);

public sealed record PlaybackSessionSnapshot(
    string SessionId,
    long ContentVersion,
    IReadOnlyList<PlaybackNodeSnapshot> Nodes,
    int Index,
    bool Paused,
    bool PlayPublished,
    DateTimeOffset UpdatedAtUtc);

public sealed class PlaybackSessionStore
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly string filePath;
    private readonly JsonSerializerOptions options = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public PlaybackSessionStore(IOptions<StorageOptions> storage, IHostEnvironment environment)
    {
        var directory = Path.GetFullPath(storage.Value.DataDirectory, environment.ContentRootPath);
        Directory.CreateDirectory(directory);
        filePath = Path.Combine(directory, "active-playback-session.json");
    }

    public async Task<PlaybackSessionSnapshot?> LoadAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(filePath)) return null;
            await using var stream = File.OpenRead(filePath);
            return await JsonSerializer.DeserializeAsync<PlaybackSessionSnapshot>(stream, options, cancellationToken);
        }
        catch (JsonException) { return null; }
        finally { gate.Release(); }
    }

    public async Task SaveAsync(PlaybackSessionSnapshot snapshot, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var tempPath = filePath + ".tmp";
            await using (var stream = File.Create(tempPath))
                await JsonSerializer.SerializeAsync(stream, snapshot, options, cancellationToken);
            File.Move(tempPath, filePath, true);
        }
        finally { gate.Release(); }
    }

    public async Task ClearAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try { if (File.Exists(filePath)) File.Delete(filePath); }
        finally { gate.Release(); }
    }
}
