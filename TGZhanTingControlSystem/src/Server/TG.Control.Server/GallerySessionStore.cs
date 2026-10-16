using System.Text.Json;
using Microsoft.Extensions.Options;
using TG.Control.Contracts;

namespace TG.Control.Server;

public sealed record GallerySessionSnapshot(
    string SessionId,
    long ContentVersion,
    string ModuleId,
    string ImageId,
    int ImageIndex,
    GallerySessionState State,
    bool SlideshowActive,
    int SlideshowIntervalSeconds,
    string? Error,
    DateTimeOffset UpdatedAtUtc);

public sealed class GallerySessionStore
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly string filePath;
    private readonly JsonSerializerOptions options = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public GallerySessionStore(IOptions<StorageOptions> storage, IHostEnvironment environment)
    {
        var directory = Path.GetFullPath(storage.Value.DataDirectory, environment.ContentRootPath);
        Directory.CreateDirectory(directory);
        filePath = Path.Combine(directory, "active-gallery-session.json");
    }

    public async Task<GallerySessionSnapshot?> LoadAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(filePath)) return null;
            await using var stream = File.OpenRead(filePath);
            return await JsonSerializer.DeserializeAsync<GallerySessionSnapshot>(stream, options, cancellationToken);
        }
        catch (JsonException)
        {
            return null;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task SaveAsync(GallerySessionSnapshot snapshot, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var tempPath = filePath + ".tmp";
            await using (var stream = File.Create(tempPath))
                await JsonSerializer.SerializeAsync(stream, snapshot, options, cancellationToken);
            File.Move(tempPath, filePath, true);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task ClearAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (File.Exists(filePath)) File.Delete(filePath);
        }
        finally
        {
            gate.Release();
        }
    }
}
