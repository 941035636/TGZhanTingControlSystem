using TG.Control.Contracts;

namespace TG.Control.Server;

public sealed record AssetDeleteResult(bool Deleted, bool Protected, IReadOnlyList<string> References);

public sealed class AssetReferenceProtectionService(
    IContentRepository publishedRepository,
    ContentDraftRepository draftRepository,
    TtsProductionRepository ttsRepository,
    AssetStorage assetStorage)
{
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task<AssetDeleteResult> DeleteIfUnreferencedAsync(string storedName,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(storedName) ||
            !string.Equals(storedName, Path.GetFileName(storedName), StringComparison.Ordinal))
            return new AssetDeleteResult(false, false, []);

        await gate.WaitAsync(cancellationToken);
        try
        {
            var references = new List<string>();
            var current = await publishedRepository.GetAsync(cancellationToken);
            CollectContentReferences(current, storedName, $"当前正式版本 V{current.Version}", references);
            foreach (var historical in await publishedRepository.GetAllVersionsAsync(cancellationToken))
                CollectContentReferences(historical, storedName, $"历史版本 V{historical.Version}", references);

            var draft = await draftRepository.GetExistingAsync(cancellationToken);
            if (draft is not null)
                CollectModuleReferences(draft.Modules, storedName, $"当前草稿 r{draft.Revision}", references);

            foreach (var candidate in await ttsRepository.GetCandidatesAsync(cancellationToken))
            {
                if (candidate.Validation.Valid && Matches(candidate.Asset.Url, storedName))
                    references.Add($"有效TTS候选 {candidate.CandidateId}");
            }

            var distinct = references.Distinct(StringComparer.OrdinalIgnoreCase).Order().ToArray();
            if (distinct.Length > 0) return new AssetDeleteResult(false, true, distinct);
            return new AssetDeleteResult(assetStorage.Delete(storedName), false, []);
        }
        finally { gate.Release(); }
    }

    private static void CollectContentReferences(PublishedContent content, string storedName, string owner,
        ICollection<string> references) => CollectModuleReferences(content.Modules, storedName, owner, references);

    private static void CollectModuleReferences(IReadOnlyList<ExhibitionModule> modules, string storedName,
        string owner, ICollection<string> references)
    {
        foreach (var module in modules)
        {
            if (Matches(module.CoverUrl, storedName)) references.Add($"{owner} / 模块“{module.Name}”封面");
            foreach (var node in module.Nodes ?? [])
            {
                foreach (var asset in node.Assets ?? [])
                {
                    if (Matches(asset.Url, storedName))
                        references.Add($"{owner} / 模块“{module.Name}” / 节点“{node.Name}” / 素材“{asset.Name}”");
                }
                if (node.NarrationAudio is not null && Matches(node.NarrationAudio.Asset.Url, storedName))
                    references.Add($"{owner} / 模块“{module.Name}” / 节点“{node.Name}” / 讲解音频绑定");
                else if (Matches(node.TtsAudioUrl, storedName))
                    references.Add($"{owner} / 模块“{module.Name}” / 节点“{node.Name}” / 旧版讲解音频");
            }
        }
    }

    private static bool Matches(string? url, string storedName)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!Uri.TryCreate(url, UriKind.RelativeOrAbsolute, out var uri)) return false;
        var path = uri.IsAbsoluteUri ? uri.AbsolutePath : url.Split('?', '#')[0];
        return string.Equals(Uri.UnescapeDataString(Path.GetFileName(path)), storedName,
            StringComparison.OrdinalIgnoreCase);
    }
}
