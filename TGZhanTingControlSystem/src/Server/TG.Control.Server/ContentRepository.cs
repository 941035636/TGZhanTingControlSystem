using System.Text.Json;
using Microsoft.Extensions.Options;
using TG.Control.Contracts;

namespace TG.Control.Server;

public interface IContentRepository
{
    Task<PublishedContent> GetAsync(CancellationToken cancellationToken);
    Task<PublishedContent> SaveAsync(IReadOnlyList<ExhibitionModule> modules, string publishedBy, CancellationToken cancellationToken);
    Task<IReadOnlyList<ContentVersionSummary>> GetHistoryAsync(CancellationToken cancellationToken);
    Task<PublishedContent> RollbackAsync(long version, string publishedBy, CancellationToken cancellationToken);
}

public sealed class JsonContentRepository : IContentRepository
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly string filePath;
    private readonly string historyDirectory;
    private readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public JsonContentRepository(IOptions<StorageOptions> options, IHostEnvironment environment)
    {
        var directory = Path.GetFullPath(options.Value.DataDirectory, environment.ContentRootPath);
        Directory.CreateDirectory(directory);
        filePath = Path.Combine(directory, "published-content.json");
        historyDirectory = Path.Combine(directory, "ContentVersions");
        Directory.CreateDirectory(historyDirectory);
    }

    public async Task<PublishedContent> GetAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(filePath))
            {
                var initial = new PublishedContent(0, DateTimeOffset.UtcNow, "system", DefaultModules.Create());
                await WriteAsync(initial, cancellationToken);
                await ArchiveAsync(initial, cancellationToken);
                return initial;
            }

            await using var stream = File.OpenRead(filePath);
            return await JsonSerializer.DeserializeAsync<PublishedContent>(stream, jsonOptions, cancellationToken)
                ?? throw new InvalidDataException("Published content file is empty or invalid.");
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<PublishedContent> SaveAsync(IReadOnlyList<ExhibitionModule> modules, string publishedBy, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var currentVersion = 0L;
            if (File.Exists(filePath))
            {
                await using var read = File.OpenRead(filePath);
                currentVersion = (await JsonSerializer.DeserializeAsync<PublishedContent>(read, jsonOptions, cancellationToken))?.Version ?? 0;
            }

            var content = new PublishedContent(currentVersion + 1, DateTimeOffset.UtcNow, publishedBy, modules);
            await WriteAsync(content, cancellationToken);
            await ArchiveAsync(content, cancellationToken);
            return content;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<ContentVersionSummary>> GetHistoryAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var current = await ReadCurrentAsync(cancellationToken);
            await ArchiveAsync(current, cancellationToken);
            var result = new List<ContentVersionSummary>();
            foreach (var path in Directory.EnumerateFiles(historyDirectory, "content-v*.json"))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await using var stream = File.OpenRead(path);
                var item = await JsonSerializer.DeserializeAsync<PublishedContent>(stream, jsonOptions, cancellationToken);
                if (item is null) continue;
                result.Add(new ContentVersionSummary(item.Version, item.PublishedAtUtc, item.PublishedBy,
                    item.Modules.Count, item.Modules.Sum(module => module.Nodes.Count), item.Version == current.Version));
            }
            return result.OrderByDescending(item => item.Version).ToArray();
        }
        finally { gate.Release(); }
    }

    public async Task<PublishedContent> RollbackAsync(long version, string publishedBy, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var path = HistoryPath(version);
            if (!File.Exists(path)) throw new KeyNotFoundException($"内容版本 V{version} 不存在。");
            await using var source = File.OpenRead(path);
            var target = await JsonSerializer.DeserializeAsync<PublishedContent>(source, jsonOptions, cancellationToken)
                ?? throw new InvalidDataException($"内容版本 V{version} 无效。");
            var current = await ReadCurrentAsync(cancellationToken);
            var restored = new PublishedContent(current.Version + 1, DateTimeOffset.UtcNow,
                publishedBy + $"（回滚自 V{version}）", target.Modules);
            await WriteAsync(restored, cancellationToken);
            await ArchiveAsync(restored, cancellationToken);
            return restored;
        }
        finally { gate.Release(); }
    }

    private async Task<PublishedContent> ReadCurrentAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath)) return new PublishedContent(0, DateTimeOffset.UtcNow, "system", DefaultModules.Create());
        await using var stream = File.OpenRead(filePath);
        return await JsonSerializer.DeserializeAsync<PublishedContent>(stream, jsonOptions, cancellationToken)
            ?? throw new InvalidDataException("Published content file is empty or invalid.");
    }

    private async Task ArchiveAsync(PublishedContent content, CancellationToken cancellationToken)
    {
        var path = HistoryPath(content.Version);
        if (File.Exists(path)) return;
        var tempPath = path + ".tmp";
        await using (var stream = File.Create(tempPath))
            await JsonSerializer.SerializeAsync(stream, content, jsonOptions, cancellationToken);
        File.Move(tempPath, path, true);
    }

    private string HistoryPath(long version) => Path.Combine(historyDirectory, $"content-v{version:D8}.json");

    private async Task WriteAsync(PublishedContent content, CancellationToken cancellationToken)
    {
        var tempPath = filePath + ".tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, content, jsonOptions, cancellationToken);
        }

        File.Move(tempPath, filePath, true);
    }
}
