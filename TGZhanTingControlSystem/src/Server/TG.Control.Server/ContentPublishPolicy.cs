using TG.Control.Contracts;

namespace TG.Control.Server;

public static class ContentPublishPolicy
{
    private const char KeySeparator = '\u001f';

    public static ContentPublishReadiness Evaluate(
        IReadOnlyList<ExhibitionModule> modules,
        AssetStorage assetStorage,
        HostString requestHost,
        PublishedContent? currentContent = null,
        LegacyNarrationAudioValidation legacyValidation = LegacyNarrationAudioValidation.PreserveOnly,
        IReadOnlyDictionary<string, NarrationAudioBindingEvaluation>? knownEvaluations = null)
    {
        ArgumentNullException.ThrowIfNull(modules);
        ArgumentNullException.ThrowIfNull(assetStorage);

        var issues = new List<ContentPublishIssue>();
        var currentNodes = (currentContent?.Modules ?? [])
            .SelectMany(module => (module.Nodes ?? []).Select(node => (Key: Key(module.Id, node.Id), Node: node)))
            .ToDictionary(item => item.Key, item => item.Node, StringComparer.OrdinalIgnoreCase);
        var assetsById = new Dictionary<string, ContentAsset>(StringComparer.OrdinalIgnoreCase);
        var assetIdsByUrl = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var counts = new int[Enum.GetValues<NarrationAudioBindingStatus>().Length];

        foreach (var module in modules)
        {
            foreach (var node in module.Nodes ?? [])
            {
                foreach (var asset in node.Assets ?? [])
                    ValidateStableAssetIdentity(module, node, asset, assetsById, assetIdsByUrl, issues, null);

                var evaluation = knownEvaluations is not null &&
                                 knownEvaluations.TryGetValue(Key(module.Id, node.Id), out var known)
                    ? known
                    : NarrationAudioBindingInspector.Evaluate(node, assetStorage, requestHost);
                var hasNarrationText = !string.IsNullOrWhiteSpace(node.NarrationText);
                var hasPlayableVisual = (node.Assets ?? []).Any(asset =>
                    asset.Kind is AssetKind.Video or AssetKind.Animation && !string.IsNullOrWhiteSpace(asset.Url));

                if (node.NarrationAudio is not null)
                {
                    counts[(int)evaluation.Status]++;
                    if (evaluation.Status != NarrationAudioBindingStatus.Fresh)
                        AddIssue(issues, module, node, StatusCode(evaluation.Status),
                            ContentPublishIssueSeverity.Error, evaluation.Message, evaluation.Status);
                    ValidateStableAssetIdentity(module, node, node.NarrationAudio.Asset,
                        assetsById, assetIdsByUrl, issues, NarrationAudioBindingStatus.InvalidAsset);
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(node.TtsAudioUrl))
                {
                    counts[(int)NarrationAudioBindingStatus.LegacyUnverified]++;
                    var storageError = assetStorage.ValidatePublishedReference(node.TtsAudioUrl, 0, requestHost);
                    if (storageError is not null)
                    {
                        AddIssue(issues, module, node, "legacy_asset_invalid", ContentPublishIssueSeverity.Error,
                            storageError, NarrationAudioBindingStatus.LegacyUnverified);
                        continue;
                    }

                    var preserved = legacyValidation == LegacyNarrationAudioValidation.AllowHistorical ||
                                    IsUnchangedLegacyNode(module.Id, node, currentNodes);
                    AddIssue(issues, module, node, preserved ? "legacy_unverified" : "legacy_upgrade_required",
                        preserved ? ContentPublishIssueSeverity.Warning : ContentPublishIssueSeverity.Error,
                        preserved
                            ? "旧版讲解音频缺少完整资产校验；本次仅兼容保留，后续编辑时必须重新生成或上传。"
                            : "历史讲解音频未绑定当前讲解词；不能跟随已修改的讲解词或新节点发布，请重新生成或上传形成完整绑定。",
                        NarrationAudioBindingStatus.LegacyUnverified);
                    continue;
                }

                if (!hasNarrationText) continue;
                counts[(int)NarrationAudioBindingStatus.Missing]++;
                AddIssue(issues, module, node,
                    hasPlayableVisual ? "video_without_narration_audio" : "narration_audio_missing",
                    hasPlayableVisual ? ContentPublishIssueSeverity.Warning : ContentPublishIssueSeverity.Error,
                    hasPlayableVisual
                        ? "该节点将按原有语义仅播放大屏素材，讲解词没有可播放语音。"
                        : "该节点只有讲解词，没有视频或可播放讲解音频，不能形成正式自动讲解。",
                    NarrationAudioBindingStatus.Missing);
            }
        }

        var blocking = issues.Count(issue => issue.Severity == ContentPublishIssueSeverity.Error);
        var warnings = issues.Count(issue => issue.Severity == ContentPublishIssueSeverity.Warning);
        return new ContentPublishReadiness(blocking == 0,
            new NarrationAudioPublishSummary(
                counts[(int)NarrationAudioBindingStatus.Fresh],
                counts[(int)NarrationAudioBindingStatus.Missing],
                counts[(int)NarrationAudioBindingStatus.StaleText],
                counts[(int)NarrationAudioBindingStatus.StaleSynthesisConfiguration],
                counts[(int)NarrationAudioBindingStatus.LegacyUnverified],
                counts[(int)NarrationAudioBindingStatus.InvalidAsset],
                counts[(int)NarrationAudioBindingStatus.InvalidBinding],
                blocking,
                warnings),
            issues);
    }

    public static string NodeKey(string moduleId, string nodeId) => Key(moduleId, nodeId);

    private static bool IsUnchangedLegacyNode(string moduleId, NarrationNode node,
        IReadOnlyDictionary<string, NarrationNode> currentNodes)
    {
        if (!currentNodes.TryGetValue(Key(moduleId, node.Id), out var currentNode) ||
            currentNode.NarrationAudio is not null)
            return false;
        return string.Equals(currentNode.TtsAudioUrl, node.TtsAudioUrl, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(NarrationAudioFingerprint.ComputeText(currentNode.NarrationText),
                   NarrationAudioFingerprint.ComputeText(node.NarrationText), StringComparison.OrdinalIgnoreCase);
    }

    private static void ValidateStableAssetIdentity(ExhibitionModule module, NarrationNode node, ContentAsset asset,
        IDictionary<string, ContentAsset> assetsById, IDictionary<string, string> assetIdsByUrl,
        ICollection<ContentPublishIssue> issues, NarrationAudioBindingStatus? narrationAudioStatus)
    {
        if (string.IsNullOrWhiteSpace(asset.Id) || string.IsNullOrWhiteSpace(asset.Url)) return;
        if (assetsById.TryGetValue(asset.Id, out var existing) && !SameIdentity(existing, asset))
        {
            AddIssue(issues, module, node, "asset_id_conflict", ContentPublishIssueSeverity.Error,
                $"资产ID {asset.Id} 在当前内容中对应了不同的URL或完整性信息。",
                narrationAudioStatus);
        }
        else
        {
            assetsById[asset.Id] = asset;
        }

        var canonicalUrl = asset.Url.Split('?', '#')[0];
        if (assetIdsByUrl.TryGetValue(canonicalUrl, out var existingId) &&
            !string.Equals(existingId, asset.Id, StringComparison.OrdinalIgnoreCase))
        {
            AddIssue(issues, module, node, "asset_url_identity_conflict", ContentPublishIssueSeverity.Error,
                $"同一不可变音频URL关联了不同资产ID（{existingId} / {asset.Id}）。",
                narrationAudioStatus);
        }
        else
        {
            assetIdsByUrl[canonicalUrl] = asset.Id;
        }
    }

    private static bool SameIdentity(ContentAsset left, ContentAsset right) =>
        string.Equals(left.Url.Split('?', '#')[0], right.Url.Split('?', '#')[0], StringComparison.OrdinalIgnoreCase) &&
        string.Equals(left.Sha256, right.Sha256, StringComparison.OrdinalIgnoreCase) &&
        left.SizeBytes == right.SizeBytes &&
        string.Equals(left.MediaType, right.MediaType, StringComparison.OrdinalIgnoreCase);

    private static string StatusCode(NarrationAudioBindingStatus status) => status switch
    {
        NarrationAudioBindingStatus.StaleText => "narration_audio_stale_text",
        NarrationAudioBindingStatus.StaleSynthesisConfiguration => "narration_audio_stale_configuration",
        NarrationAudioBindingStatus.InvalidAsset => "narration_audio_invalid_asset",
        NarrationAudioBindingStatus.InvalidBinding => "narration_audio_invalid_binding",
        _ => "narration_audio_invalid"
    };

    private static void AddIssue(ICollection<ContentPublishIssue> issues, ExhibitionModule module,
        NarrationNode node, string code, ContentPublishIssueSeverity severity, string detail,
        NarrationAudioBindingStatus? status)
    {
        issues.Add(new ContentPublishIssue(module.Id, node.Id, module.Name, node.Name, code, severity,
            $"模块“{module.Name}” / 节点“{node.Name}”：{detail}", status));
    }

    private static string Key(string moduleId, string nodeId) => moduleId + KeySeparator + nodeId;
}
