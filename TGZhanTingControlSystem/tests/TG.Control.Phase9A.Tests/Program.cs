using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using TG.Control.Contracts;
using TG.Control.Server;

namespace TG.Control.Phase9A.Tests;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private static async Task<int> Main()
    {
        using var context = TestContext.Create();
        var tests = new (string Name, Func<Task> Run)[]
        {
            ("Narration text fingerprint is stable", () => Run(() => TextFingerprintIsStable())),
            ("Narration text change changes fingerprint", () => Run(() => TextChangeChangesFingerprint())),
            ("Synthesis configuration property order is stable", () => Run(() => ConfigurationOrderIsStable())),
            ("Provider voice language rate and pitch change configuration fingerprint", () => Run(() => ConfigurationChangesFingerprint())),
            ("Manual binding is complete", () => Run(() => ManualBindingIsComplete(context))),
            ("Fresh binding is detected", () => Run(() => BindingIsFresh(context))),
            ("Content validator accepts a fresh binding", () => Run(() => ContentValidatorAcceptsFreshBinding(context))),
            ("Narration text change makes binding stale", () => Run(() => TextChangeMakesBindingStale(context))),
            ("Content validator rejects a stale binding", () => Run(() => ContentValidatorRejectsStaleBinding(context))),
            ("Voice change makes binding stale", () => Run(() => VoiceChangeMakesBindingStale(context))),
            ("Missing SHA is invalid asset", () => Run(() => MissingShaIsInvalid(context))),
            ("Wrong SHA is invalid asset", () => Run(() => WrongShaIsInvalid(context))),
            ("Missing size is invalid asset", () => Run(() => MissingSizeIsInvalid(context))),
            ("Legacy JSON deserializes", () => Run(LegacyJsonDeserializes)),
            ("New JSON round-trips", () => Run(() => NewJsonRoundTrips(context))),
            ("Rollback reads legacy content", () => RollbackReadsLegacyContent(context)),
            ("LED manifest uses binding SHA and size", () => Run(() => ManifestUsesBindingIntegrity(context))),
            ("LED manifest marks legacy integrity as unavailable", () => Run(() => ManifestKeepsExplicitLegacyFallback(context))),
            ("Legacy audio cannot follow changed narration text", () => Run(() => LegacyTextChangeIsRejected(context))),
            ("New binding URL has priority over legacy URL", () => Run(() => BindingUrlHasPriority(context)))
        };

        var failed = 0;
        foreach (var test in tests)
        {
            try
            {
                await test.Run();
                Console.WriteLine($"PASS  {test.Name}");
            }
            catch (Exception exception)
            {
                failed++;
                Console.Error.WriteLine($"FAIL  {test.Name}: {exception.Message}");
            }
        }

        Console.WriteLine($"Phase 9A tests: {tests.Length - failed}/{tests.Length} passed.");
        return failed == 0 ? 0 : 1;
    }

    private static void TextFingerprintIsStable()
    {
        var composed = NarrationAudioFingerprint.ComputeText("  Café\r\n智慧展厅  ");
        var decomposed = NarrationAudioFingerprint.ComputeText("Cafe\u0301\n智慧展厅");
        Equal(composed, decomposed);
    }

    private static void TextChangeChangesFingerprint() =>
        NotEqual(NarrationAudioFingerprint.ComputeText("文案 A。"), NarrationAudioFingerprint.ComputeText("文案 B。"));

    private static void ConfigurationOrderIsStable()
    {
        const string first = """
            {"providerKey":"provider","voice":"voice-1","language":"zh-CN","rate":1.1,"pitch":0.2,"volume":0.8,"outputMediaType":"audio/mpeg","sampleRateHz":24000,"channels":1}
            """;
        const string second = """
            {"channels":1,"sampleRateHz":24000,"outputMediaType":"audio/mpeg","volume":0.8,"pitch":0.2,"rate":1.1,"language":"zh-CN","voice":"voice-1","providerKey":"provider"}
            """;
        var a = JsonSerializer.Deserialize<TtsSynthesisConfiguration>(first, JsonOptions)!;
        var b = JsonSerializer.Deserialize<TtsSynthesisConfiguration>(second, JsonOptions)!;
        Equal(NarrationAudioFingerprint.ComputeSynthesisConfiguration(a),
            NarrationAudioFingerprint.ComputeSynthesisConfiguration(b));
    }

    private static void ConfigurationChangesFingerprint()
    {
        var baseline = Configuration();
        var baselineFingerprint = NarrationAudioFingerprint.ComputeSynthesisConfiguration(baseline);
        NotEqual(baselineFingerprint, NarrationAudioFingerprint.ComputeSynthesisConfiguration(baseline with { ProviderKey = "provider-2" }));
        NotEqual(baselineFingerprint, NarrationAudioFingerprint.ComputeSynthesisConfiguration(baseline with { Voice = "voice-2" }));
        NotEqual(baselineFingerprint, NarrationAudioFingerprint.ComputeSynthesisConfiguration(baseline with { Language = "en-US" }));
        NotEqual(baselineFingerprint, NarrationAudioFingerprint.ComputeSynthesisConfiguration(baseline with { Rate = 1.2 }));
        NotEqual(baselineFingerprint, NarrationAudioFingerprint.ComputeSynthesisConfiguration(baseline with { Pitch = 0.1 }));
    }

    private static void ManualBindingIsComplete(TestContext context)
    {
        var binding = context.CreateManualBinding("人工讲解词");
        True(!string.IsNullOrWhiteSpace(binding.Asset.Id), "Asset ID is missing.");
        True(!string.IsNullOrWhiteSpace(binding.Asset.Url), "Asset URL is missing.");
        True(NarrationAudioFingerprint.IsSha256(binding.Asset.Sha256), "Asset SHA-256 is missing.");
        True(binding.Asset.SizeBytes > 0, "Asset size is missing.");
        Equal("audio/mpeg", binding.Asset.MediaType);
        Equal(NarrationAudioOrigin.ManualUpload, binding.Origin);
    }

    private static void BindingIsFresh(TestContext context)
    {
        const string text = "欢迎参观智慧展厅。";
        var binding = context.CreateManualBinding(text);
        Equal(NarrationAudioBindingStatus.Fresh,
            NarrationAudioBindingInspector.Evaluate(Node(text, binding), context.Storage, context.Host).Status);
    }

    private static void ContentValidatorAcceptsFreshBinding(TestContext context)
    {
        const string text = "发布Fresh绑定";
        var binding = context.CreateManualBinding(text);
        var errors = ContentValidator.Validate(Content(Node(text, binding), 0).Modules,
            context.Storage, context.Host, new PublishedContent(0, DateTimeOffset.UtcNow, "system", []));
        Equal(0, errors.Count);
    }

    private static void TextChangeMakesBindingStale(TestContext context)
    {
        var binding = context.CreateManualBinding("文案 A");
        Equal(NarrationAudioBindingStatus.StaleText,
            NarrationAudioBindingInspector.Evaluate(Node("文案 B", binding), context.Storage, context.Host).Status);
    }

    private static void ContentValidatorRejectsStaleBinding(TestContext context)
    {
        var binding = context.CreateManualBinding("文案 A");
        var errors = ContentValidator.Validate(Content(Node("文案 B", binding), 0).Modules,
            context.Storage, context.Host, new PublishedContent(0, DateTimeOffset.UtcNow, "system", []));
        True(errors.Values.SelectMany(value => value).Any(message => message.Contains("讲解词已修改", StringComparison.Ordinal)),
            "ContentValidator accepted a stale narration audio binding.");
    }

    private static void VoiceChangeMakesBindingStale(TestContext context)
    {
        const string text = "音色变化测试";
        var binding = context.CreateBinding(text, Configuration());
        var node = Node(text, binding) with { TtsConfiguration = binding.SynthesisConfiguration with { Voice = "voice-2" } };
        Equal(NarrationAudioBindingStatus.StaleSynthesisConfiguration,
            NarrationAudioBindingInspector.Evaluate(node, context.Storage, context.Host).Status);
    }

    private static void MissingShaIsInvalid(TestContext context)
    {
        var binding = context.CreateManualBinding("SHA测试");
        var invalid = binding with { Asset = binding.Asset with { Sha256 = string.Empty } };
        Equal(NarrationAudioBindingStatus.InvalidAsset,
            NarrationAudioBindingInspector.Evaluate(Node("SHA测试", invalid), context.Storage, context.Host).Status);
    }

    private static void WrongShaIsInvalid(TestContext context)
    {
        var binding = context.CreateManualBinding("SHA错误测试");
        var invalid = binding with { Asset = binding.Asset with { Sha256 = new string('0', 64) } };
        Equal(NarrationAudioBindingStatus.InvalidAsset,
            NarrationAudioBindingInspector.Evaluate(Node("SHA错误测试", invalid), context.Storage, context.Host).Status);
    }

    private static void MissingSizeIsInvalid(TestContext context)
    {
        var binding = context.CreateManualBinding("Size测试");
        var invalid = binding with { Asset = binding.Asset with { SizeBytes = 0 } };
        Equal(NarrationAudioBindingStatus.InvalidAsset,
            NarrationAudioBindingInspector.Evaluate(Node("Size测试", invalid), context.Storage, context.Host).Status);
    }

    private static void LegacyJsonDeserializes()
    {
        const string json = """
            {
              "version": 7,
              "publishedAtUtc": "2026-08-31T00:00:00Z",
              "publishedBy": "legacy",
              "modules": [{
                "id": "module-01", "name": "历史模块", "order": 1, "description": "", "coverUrl": null, "enabled": true,
                "nodes": [{
                  "id": "node-01", "name": "历史节点", "order": 1, "narrationText": "旧讲解词",
                  "ttsAudioUrl": "/media/legacy.mp3", "assets": [], "failurePolicy": 0,
                  "audioMixPolicy": 0, "videoVolume": 0.25, "narrationVolume": 1.0
                }]
              }]
            }
            """;
        var content = JsonSerializer.Deserialize<PublishedContent>(json, JsonOptions)!;
        Equal("/media/legacy.mp3", content.Modules[0].Nodes[0].TtsAudioUrl);
        True(content.Modules[0].Nodes[0].NarrationAudio is null, "Legacy JSON unexpectedly created a binding.");
    }

    private static void NewJsonRoundTrips(TestContext context)
    {
        const string text = "新模型序列化";
        var binding = context.CreateManualBinding(text);
        var content = Content(Node(text, binding), 9);
        var restored = JsonSerializer.Deserialize<PublishedContent>(JsonSerializer.Serialize(content, JsonOptions), JsonOptions)!;
        var restoredBinding = restored.Modules[0].Nodes[0].NarrationAudio!;
        Equal(binding.Asset.Id, restoredBinding.Asset.Id);
        Equal(binding.Asset.Sha256, restoredBinding.Asset.Sha256);
        Equal(binding.NarrationTextFingerprint, restoredBinding.NarrationTextFingerprint);
        Equal(binding.SynthesisConfigurationFingerprint, restoredBinding.SynthesisConfigurationFingerprint);
    }

    private static async Task RollbackReadsLegacyContent(TestContext context)
    {
        var repository = context.Repository;
        _ = await repository.GetAsync(CancellationToken.None);
        var legacy = LegacyNode("旧版本讲解词", context.Asset.Url);
        var v1 = await repository.SaveAsync(Content(legacy, 0).Modules, "test", CancellationToken.None);
        var binding = context.CreateManualBinding("新版本讲解词");
        _ = await repository.SaveAsync(Content(Node("新版本讲解词", binding), 0).Modules, "test", CancellationToken.None);
        var restored = await repository.RollbackAsync(v1.Version, "test", CancellationToken.None);
        var restoredNode = restored.Modules[0].Nodes[0];
        Equal(context.Asset.Url, restoredNode.TtsAudioUrl);
        True(restoredNode.NarrationAudio is null, "Legacy rollback unexpectedly created a verified binding.");
    }

    private static void ManifestUsesBindingIntegrity(TestContext context)
    {
        var binding = context.CreateManualBinding("清单测试");
        var manifest = ContentManifestBuilder.Build(Content(Node("清单测试", binding), 11));
        var audio = manifest.Assets.Single(asset => asset.Url == binding.Asset.Url);
        Equal(binding.Asset.Sha256, audio.Sha256);
        Equal(binding.Asset.SizeBytes, audio.SizeBytes);
    }

    private static void ManifestKeepsExplicitLegacyFallback(TestContext context)
    {
        var manifest = ContentManifestBuilder.Build(Content(LegacyNode("旧内容", context.Asset.Url), 12));
        var audio = manifest.Assets.Single(asset => asset.Url == context.Asset.Url);
        Equal(string.Empty, audio.Sha256);
        Equal(0L, audio.SizeBytes);
    }

    private static void LegacyTextChangeIsRejected(TestContext context)
    {
        var current = Content(LegacyNode("文案 A", context.Asset.Url), 3);
        var edited = Content(LegacyNode("文案 B", context.Asset.Url), 3).Modules;
        var errors = ContentValidator.Validate(edited, context.Storage, context.Host, current);
        True(errors.Values.SelectMany(value => value).Any(message => message.Contains("历史讲解音频未绑定当前讲解词", StringComparison.Ordinal)),
            "Changed legacy narration text was not rejected.");
    }

    private static void BindingUrlHasPriority(TestContext context)
    {
        var binding = context.CreateManualBinding("优先级测试");
        var normalized = NarrationAudioCompatibility.NormalizeNode(Node("优先级测试", binding) with { TtsAudioUrl = "/media/old.mp3" });
        Equal(binding.Asset.Url, normalized.TtsAudioUrl);
    }

    private static TtsSynthesisConfiguration Configuration() =>
        new("provider", "voice-1", "zh-CN", 1, 0, 1, "audio/mpeg", 24000, 1);

    private static NarrationNode Node(string text, NarrationAudioBinding binding) =>
        new("node-01", "讲解节点", 1, text, binding.Asset.Url, [], FailurePolicy.Skip, AudioMixPolicy.Duck,
            0.25, 1, binding.SynthesisConfiguration, binding);

    private static NarrationNode LegacyNode(string text, string audioUrl) =>
        new("node-01", "讲解节点", 1, text, audioUrl, [], FailurePolicy.Skip, AudioMixPolicy.Duck, 0.25, 1);

    private static PublishedContent Content(NarrationNode node, long version) =>
        new(version, DateTimeOffset.UtcNow, "test",
            [new ExhibitionModule("module-01", "测试模块", 1, string.Empty, null, true, [node])]);

    private static Task Run(Action action)
    {
        action();
        return Task.CompletedTask;
    }

    private static void True(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
    }

    private static void NotEqual<T>(T notExpected, T actual)
    {
        if (EqualityComparer<T>.Default.Equals(notExpected, actual))
            throw new InvalidOperationException($"Did not expect '{actual}'.");
    }

    private sealed class TestContext : IDisposable
    {
        private readonly string rootPath;

        private TestContext(string rootPath, AssetStorage storage, ContentAsset asset, JsonContentRepository repository)
        {
            this.rootPath = rootPath;
            Storage = storage;
            Asset = asset;
            Repository = repository;
        }

        public AssetStorage Storage { get; }
        public ContentAsset Asset { get; }
        public JsonContentRepository Repository { get; }
        public HostString Host { get; } = new("localhost");

        public static TestContext Create()
        {
            var parent = Path.Combine(Path.GetTempPath(), "TG.Control.Phase9A.Tests");
            var root = Path.Combine(parent, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var environment = new TestHostEnvironment { ContentRootPath = root };
            var options = Options.Create(new StorageOptions { DataDirectory = "Data" });
            var storage = new AssetStorage(options, environment);
            var bytes = "phase-9a-test-mp3"u8.ToArray();
            var storedName = "narration-test.mp3";
            File.WriteAllBytes(Path.Combine(storage.MediaDirectory, storedName), bytes);
            var asset = new ContentAsset("asset-audio-01", "narration-test.mp3", AssetKind.NarrationAudio,
                "/media/" + storedName, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
                bytes.Length, 1.5, "audio/mpeg");
            var repository = new JsonContentRepository(options, environment);
            return new TestContext(root, storage, asset, repository);
        }

        public NarrationAudioBinding CreateManualBinding(string text) =>
            new NarrationAudioBindingService(Storage).CreateManualBinding(
                new CreateManualNarrationAudioBindingRequest(Asset, text), Host);

        public NarrationAudioBinding CreateBinding(string text, TtsSynthesisConfiguration configuration) =>
            new(Asset, NarrationAudioFingerprint.ComputeText(text),
                NarrationAudioFingerprint.ComputeSynthesisConfiguration(configuration), configuration,
                NarrationAudioOrigin.Generated, DateTimeOffset.UtcNow, NarrationAudioFingerprint.Version, "provider-request");

        public void Dispose()
        {
            var allowedParent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "TG.Control.Phase9A.Tests"));
            var resolved = Path.GetFullPath(rootPath);
            if (resolved.StartsWith(allowedParent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                Directory.Exists(resolved))
                Directory.Delete(resolved, true);
        }
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "TG.Control.Phase9A.Tests";
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
