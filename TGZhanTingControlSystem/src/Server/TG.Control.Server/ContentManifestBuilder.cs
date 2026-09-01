using TG.Control.Contracts;

namespace TG.Control.Server;

public static class ContentManifestBuilder
{
    public static ContentSyncManifest Build(PublishedContent content)
    {
        var candidates = content.Modules
            .SelectMany(module => module.Nodes ?? [])
            .SelectMany(NodeAssets)
            .Where(asset => !string.IsNullOrWhiteSpace(asset.Url))
            .ToArray();
        var assets = candidates
            .GroupBy(AssetIdentityKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => ValidateAndSelect(group.Key, group))
            .ToArray();
        return new ContentSyncManifest(content.Version, assets);
    }

    private static IEnumerable<ContentSyncAsset> NodeAssets(NarrationNode node)
    {
        foreach (var asset in node.Assets ?? [])
            yield return new ContentSyncAsset(asset.Url, asset.Sha256, asset.SizeBytes,
                asset.Id, asset.MediaType);

        if (node.NarrationAudio is not null)
        {
            if (!NarrationAudioBindingInspector.HasCompleteAssetIdentity(node.NarrationAudio.Asset, out var error))
                throw new InvalidDataException($"讲解音频绑定无效，无法生成LED素材清单：{error}");
            yield return new ContentSyncAsset(node.NarrationAudio.Asset.Url,
                node.NarrationAudio.Asset.Sha256, node.NarrationAudio.Asset.SizeBytes,
                node.NarrationAudio.Asset.Id, node.NarrationAudio.Asset.MediaType);
            yield break;
        }

        // Legacy content remains downloadable, but the empty integrity fields explicitly mean
        // that the historical URL has not been upgraded to an immutable verified asset binding.
        if (!string.IsNullOrWhiteSpace(node.TtsAudioUrl))
            yield return new ContentSyncAsset(node.TtsAudioUrl, string.Empty, 0, null, null);
    }

    private static string AssetIdentityKey(ContentSyncAsset asset) =>
        !string.IsNullOrWhiteSpace(asset.AssetId) ? "asset:" + asset.AssetId : "legacy:" + asset.Url;

    private static ContentSyncAsset ValidateAndSelect(string key, IEnumerable<ContentSyncAsset> group)
    {
        var assets = group.ToArray();
        var first = assets[0];
        if (key.StartsWith("asset:", StringComparison.OrdinalIgnoreCase) && assets.Skip(1).Any(asset =>
                !string.Equals(asset.Url, first.Url, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(asset.Sha256, first.Sha256, StringComparison.OrdinalIgnoreCase) ||
                asset.SizeBytes != first.SizeBytes ||
                !string.Equals(asset.MediaType, first.MediaType, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException($"不可变资产身份冲突：{first.AssetId}");
        return first;
    }
}
