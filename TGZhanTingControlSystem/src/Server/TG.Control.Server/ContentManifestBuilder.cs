using TG.Control.Contracts;

namespace TG.Control.Server;

public static class ContentManifestBuilder
{
    public static ContentSyncManifest Build(PublishedContent content)
    {
        var assets = content.Modules
            .SelectMany(module => module.Nodes ?? [])
            .SelectMany(NodeAssets)
            .Where(asset => !string.IsNullOrWhiteSpace(asset.Url))
            .GroupBy(asset => asset.Url, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
        return new ContentSyncManifest(content.Version, assets);
    }

    private static IEnumerable<ContentSyncAsset> NodeAssets(NarrationNode node)
    {
        foreach (var asset in node.Assets ?? [])
            yield return new ContentSyncAsset(asset.Url, asset.Sha256, asset.SizeBytes);

        if (node.NarrationAudio is not null)
        {
            if (!NarrationAudioBindingInspector.HasCompleteAssetIdentity(node.NarrationAudio.Asset, out var error))
                throw new InvalidDataException($"讲解音频绑定无效，无法生成LED素材清单：{error}");
            yield return new ContentSyncAsset(node.NarrationAudio.Asset.Url,
                node.NarrationAudio.Asset.Sha256, node.NarrationAudio.Asset.SizeBytes);
            yield break;
        }

        // Legacy content remains downloadable, but the empty integrity fields explicitly mean
        // that the historical URL has not been upgraded to an immutable verified asset binding.
        if (!string.IsNullOrWhiteSpace(node.TtsAudioUrl))
            yield return new ContentSyncAsset(node.TtsAudioUrl, string.Empty, 0);
    }
}
