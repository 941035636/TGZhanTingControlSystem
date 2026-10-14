using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using TG.Control.Contracts;
using TG.Control.Server;

namespace TG.Control.GalleryA.Tests;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<int> Main()
    {
        using var context = TestContext.Create();
        var tests = new (string Name, Func<Task> Execute)[]
        {
            ("legacy JSON normalizes missing gallery", () => Run(LegacyJsonNormalizesMissingGallery)),
            ("legacy draft normalizes missing gallery", () => LegacyDraftNormalizesMissingGallery(context)),
            ("gallery JSON preserves order and names", () => Run(() => GalleryJsonPreservesOrder(context))),
            ("manifest includes and deduplicates gallery images", () => Run(() => ManifestIncludesGallery(context))),
            ("publish accepts valid JPG and PNG gallery", () => Run(() => ValidGalleryPublishes(context))),
            ("publish rejects non-image and WebP gallery", () => Run(() => InvalidGalleryFormatsAreRejected(context))),
            ("publish rejects incomplete or corrupt integrity", () => Run(() => InvalidIntegrityIsRejected(context))),
            ("referenced gallery image is protected from deletion", () => ReferencedGalleryImageIsProtected(context)),
            ("rollback restores historical gallery order", () => RollbackRestoresGallery(context))
        };

        foreach (var test in tests)
        {
            try
            {
                await test.Execute();
                Console.WriteLine($"PASS {test.Name}");
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine($"FAIL {test.Name}: {exception.Message}");
                return 1;
            }
        }
        return 0;
    }

    private static void LegacyJsonNormalizesMissingGallery()
    {
        const string json = """
        {"version":1,"publishedAtUtc":"2026-10-15T00:00:00Z","publishedBy":"test","modules":[{"id":"m1","name":"旧模块","order":1,"description":"","coverUrl":null,"enabled":true,"nodes":[]}]}
        """;
        var content = JsonSerializer.Deserialize<PublishedContent>(json, JsonOptions)!;
        var normalized = NarrationAudioCompatibility.Normalize(content);
        True(normalized.Modules[0].GalleryImages is { Count: 0 }, "旧内容缺少图集字段时没有归一化为空数组。");
    }

    private static async Task LegacyDraftNormalizesMissingGallery(TestContext context)
    {
        var dataDirectory = Directory.GetParent(context.Storage.MediaDirectory)!.FullName;
        var path = Path.Combine(dataDirectory, "content-draft.json");
        await File.WriteAllTextAsync(path,
            "{\"baseContentVersion\":0,\"revision\":1,\"updatedAtUtc\":\"2026-10-15T00:00:00Z\",\"updatedBy\":\"test\",\"modules\":[{\"id\":\"m1\",\"name\":\"旧草稿\",\"order\":1,\"description\":\"\",\"coverUrl\":null,\"enabled\":true,\"nodes\":[]}]}");
        var draft = await context.Drafts.GetExistingAsync(CancellationToken.None);
        True(draft?.Modules[0].GalleryImages is { Count: 0 }, "旧草稿缺少图集字段时没有归一化为空数组。");
    }

    private static void GalleryJsonPreservesOrder(TestContext context)
    {
        var source = Content([context.Png with { Name = "第二张" }, context.Jpg with { Name = "第一张" }]);
        var restored = JsonSerializer.Deserialize<PublishedContent>(JsonSerializer.Serialize(source, JsonOptions), JsonOptions)!;
        var gallery = NarrationAudioCompatibility.Normalize(restored).Modules[0].GalleryImages!;
        Equal("第二张", gallery[0].Name);
        Equal(context.Jpg.Id, gallery[1].Id);
    }

    private static void ManifestIncludesGallery(TestContext context)
    {
        var modules = new[]
        {
            Module("m1", [context.Jpg, context.Png]),
            Module("m2", [context.Jpg])
        };
        var manifest = ContentManifestBuilder.Build(new PublishedContent(7, DateTimeOffset.UtcNow, "test", modules));
        Equal(2, manifest.Assets.Count);
        var jpg = manifest.Assets.Single(asset => asset.AssetId == context.Jpg.Id);
        Equal(context.Jpg.Sha256, jpg.Sha256);
        Equal(context.Jpg.SizeBytes, jpg.SizeBytes);
    }

    private static void ValidGalleryPublishes(TestContext context)
    {
        var readiness = ContentPublishPolicy.Evaluate([Module("m1", [context.Jpg, context.Png])],
            context.Storage, context.Host);
        True(readiness.CanPublish, string.Join(" | ", readiness.Issues.Select(issue => issue.Message)));
    }

    private static void InvalidGalleryFormatsAreRejected(TestContext context)
    {
        var video = context.Jpg with { Id = "video", Kind = AssetKind.Video };
        var webp = context.Webp;
        var readiness = ContentPublishPolicy.Evaluate([Module("m1", [video, webp])], context.Storage, context.Host);
        True(!readiness.CanPublish, "非图片类型和 WebP 图集未阻止发布。");
        True(readiness.Issues.Any(issue => issue.Code == "gallery_asset_kind_invalid"), "缺少图片类型错误。");
        True(readiness.Issues.Any(issue => issue.Code == "gallery_asset_format_invalid"), "缺少格式错误。");
    }

    private static void InvalidIntegrityIsRejected(TestContext context)
    {
        var missing = context.Png with { Id = "missing-integrity", Sha256 = string.Empty, SizeBytes = 0 };
        var corrupt = context.Jpg with { Id = "corrupt", Sha256 = new string('0', 64) };
        var readiness = ContentPublishPolicy.Evaluate([Module("m1", [missing, corrupt])], context.Storage, context.Host);
        True(!readiness.CanPublish, "缺失或错误完整性信息未阻止发布。");
        True(readiness.Issues.Any(issue => issue.Code == "gallery_asset_integrity_missing"), "缺少完整性缺失错误。");
        True(readiness.Issues.Any(issue => issue.Code == "gallery_asset_invalid"), "缺少 SHA 错误。");
    }

    private static async Task RollbackRestoresGallery(TestContext context)
    {
        _ = await context.Repository.GetAsync(CancellationToken.None);
        var versionA = await context.Repository.SaveAsync([Module("m1", [context.Png, context.Jpg])], "test", CancellationToken.None);
        _ = await context.Repository.SaveAsync([Module("m1", [context.Jpg])], "test", CancellationToken.None);
        var restored = await context.Repository.RollbackAsync(versionA.Version, "test", CancellationToken.None);
        var gallery = restored.Modules[0].GalleryImages!;
        Equal(2, gallery.Count);
        Equal(context.Png.Id, gallery[0].Id);
        Equal(context.Jpg.Id, gallery[1].Id);
    }

    private static async Task ReferencedGalleryImageIsProtected(TestContext context)
    {
        _ = await context.Repository.SaveAsync([Module("protected", [context.Jpg])], "test", CancellationToken.None);
        var result = await context.Protection.DeleteIfUnreferencedAsync("gallery-a.jpg", CancellationToken.None);
        True(result.Protected && !result.Deleted, "正式内容引用的图集图片仍被删除。 ");
        True(result.References.Any(reference => reference.Contains("图片图集", StringComparison.Ordinal)),
            "素材删除保护没有报告图集引用来源。");
    }

    private static PublishedContent Content(IReadOnlyList<ContentAsset> gallery) =>
        new(1, DateTimeOffset.UtcNow, "test", [Module("m1", gallery)]);

    private static ExhibitionModule Module(string id, IReadOnlyList<ContentAsset> gallery) =>
        new(id, "测试模块", 1, string.Empty, null, true, [], gallery);

    private static Task Run(Action action) { action(); return Task.CompletedTask; }
    private static void True(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
    }

    private sealed class TestContext : IDisposable
    {
        private readonly string rootPath;
        private TestContext(string rootPath, AssetStorage storage, ContentAsset jpg, ContentAsset png,
            ContentAsset webp, JsonContentRepository repository, ContentDraftRepository drafts,
            AssetReferenceProtectionService protection)
        {
            this.rootPath = rootPath; Storage = storage; Jpg = jpg; Png = png; Webp = webp;
            Repository = repository; Drafts = drafts; Protection = protection;
        }
        public AssetStorage Storage { get; }
        public ContentAsset Jpg { get; }
        public ContentAsset Png { get; }
        public ContentAsset Webp { get; }
        public JsonContentRepository Repository { get; }
        public ContentDraftRepository Drafts { get; }
        public AssetReferenceProtectionService Protection { get; }
        public HostString Host { get; } = new("localhost");

        public static TestContext Create()
        {
            var parent = Path.Combine(Path.GetTempPath(), "TG.Control.GalleryA.Tests");
            var root = Path.Combine(parent, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var environment = new TestHostEnvironment { ContentRootPath = root };
            var options = Options.Create(new StorageOptions { DataDirectory = "Data" });
            var storage = new AssetStorage(options, environment);
            var jpg = Save(storage, "gallery-a.jpg", "image/jpeg", "gallery-a-jpg"u8.ToArray());
            var png = Save(storage, "gallery-b.png", "image/png", "gallery-b-png"u8.ToArray());
            var webp = Save(storage, "gallery-c.webp", "image/webp", "gallery-c-webp"u8.ToArray());
            var repository = new JsonContentRepository(options, environment);
            var drafts = new ContentDraftRepository(options, environment);
            var tts = new TtsProductionRepository(options, environment);
            var protection = new AssetReferenceProtectionService(repository, drafts, tts, storage);
            return new TestContext(root, storage, jpg, png, webp, repository, drafts, protection);
        }

        private static ContentAsset Save(AssetStorage storage, string name, string mediaType, byte[] bytes)
        {
            File.WriteAllBytes(Path.Combine(storage.MediaDirectory, name), bytes);
            return new ContentAsset(Path.GetFileNameWithoutExtension(name), name, AssetKind.Image, "/media/" + name,
                Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), bytes.Length, 0, mediaType);
        }

        public void Dispose()
        {
            var parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "TG.Control.GalleryA.Tests"));
            var resolved = Path.GetFullPath(rootPath);
            if (resolved.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && Directory.Exists(resolved))
                Directory.Delete(resolved, true);
        }
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "TG.Control.GalleryA.Tests";
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
