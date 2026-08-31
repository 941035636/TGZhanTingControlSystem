using System.Text.Json;
using Microsoft.Extensions.Options;
using TG.Control.Contracts;

namespace TG.Control.Server;

public sealed record ContentDraftDocument(
    long BaseContentVersion,
    long Revision,
    DateTimeOffset UpdatedAtUtc,
    string UpdatedBy,
    IReadOnlyList<ExhibitionModule> Modules);

public sealed class ContentDraftConflictException(string message) : InvalidOperationException(message);

public sealed class ContentDraftRepository
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly string filePath;
    private readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public ContentDraftRepository(IOptions<StorageOptions> options, IHostEnvironment environment)
    {
        var directory = Path.GetFullPath(options.Value.DataDirectory, environment.ContentRootPath);
        Directory.CreateDirectory(directory);
        filePath = Path.Combine(directory, "content-draft.json");
    }

    public async Task<ContentDraftDocument> GetOrCreateAsync(PublishedContent published,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var draft = await ReadAsync(cancellationToken);
            if (draft is not null && draft.BaseContentVersion == published.Version) return draft;
            draft = FromPublished(published);
            await WriteAsync(draft, cancellationToken);
            return draft;
        }
        finally { gate.Release(); }
    }

    public async Task<ContentDraftDocument> ReplaceAsync(long expectedBaseVersion, long expectedRevision,
        IReadOnlyList<ExhibitionModule> modules, string updatedBy, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var draft = await ReadAsync(cancellationToken)
                        ?? throw new ContentDraftConflictException("Content draft does not exist.");
            if (draft.BaseContentVersion != expectedBaseVersion || draft.Revision != expectedRevision)
                throw new ContentDraftConflictException("Content draft changed; refresh before continuing.");
            var updated = new ContentDraftDocument(draft.BaseContentVersion, checked(draft.Revision + 1),
                DateTimeOffset.UtcNow, updatedBy, NarrationAudioCompatibility.NormalizeModules(modules));
            await WriteAsync(updated, cancellationToken);
            return updated;
        }
        finally { gate.Release(); }
    }

    public async Task<ContentDraftDocument> ResetAsync(PublishedContent published, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var draft = FromPublished(published);
            await WriteAsync(draft, cancellationToken);
            return draft;
        }
        finally { gate.Release(); }
    }

    private static ContentDraftDocument FromPublished(PublishedContent published) =>
        new(published.Version, 0, DateTimeOffset.UtcNow, published.PublishedBy,
            NarrationAudioCompatibility.NormalizeModules(published.Modules));

    private async Task<ContentDraftDocument?> ReadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath)) return null;
        await using var stream = File.OpenRead(filePath);
        return await JsonSerializer.DeserializeAsync<ContentDraftDocument>(stream, jsonOptions, cancellationToken)
               ?? throw new InvalidDataException("Content draft file is empty or invalid.");
    }

    private async Task WriteAsync(ContentDraftDocument draft, CancellationToken cancellationToken)
    {
        var tempPath = filePath + ".tmp";
        await using (var stream = File.Create(tempPath))
            await JsonSerializer.SerializeAsync(stream, draft, jsonOptions, cancellationToken);
        File.Move(tempPath, filePath, true);
    }
}
