using System.Text.Json;
using Microsoft.Extensions.Options;
using TG.Control.Contracts;

namespace TG.Control.Server;

public interface IContentRepository
{
    Task<PublishedContent> GetAsync(CancellationToken cancellationToken);
    Task<PublishedContent> SaveAsync(IReadOnlyList<ExhibitionModule> modules, string publishedBy, CancellationToken cancellationToken);
}

public sealed class JsonContentRepository : IContentRepository
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly string filePath;
    private readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public JsonContentRepository(IOptions<StorageOptions> options, IHostEnvironment environment)
    {
        var directory = Path.GetFullPath(options.Value.DataDirectory, environment.ContentRootPath);
        Directory.CreateDirectory(directory);
        filePath = Path.Combine(directory, "published-content.json");
    }

    public async Task<PublishedContent> GetAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(filePath))
            {
                var initial = new PublishedContent(0, DateTimeOffset.MinValue, "system", DefaultModules.Create());
                await WriteAsync(initial, cancellationToken);
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
            return content;
        }
        finally
        {
            gate.Release();
        }
    }

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
