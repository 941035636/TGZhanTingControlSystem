using TG.Control.Contracts;

namespace TG.Control.Server;

public enum LegacyNarrationAudioValidation
{
    PreserveOnly,
    AllowHistorical
}

public static class ContentValidator
{
    public static Dictionary<string, string[]> Validate(IReadOnlyList<ExhibitionModule>? modules,
        AssetStorage assetStorage, HostString requestHost, PublishedContent? currentContent = null,
        LegacyNarrationAudioValidation legacyValidation = LegacyNarrationAudioValidation.PreserveOnly)
    {
        var errors = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        void Add(string key, string message)
        {
            if (!errors.TryGetValue(key, out var values)) errors[key] = values = [];
            values.Add(message);
        }

        if (modules is null || modules.Count == 0)
        {
            Add("modules", "至少需要一个讲解模块。");
            return errors.ToDictionary(item => item.Key, item => item.Value.ToArray());
        }

        var moduleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var module in modules)
        {
            var moduleKey = $"modules[{module.Order}]";
            if (string.IsNullOrWhiteSpace(module.Id) || !moduleIds.Add(module.Id)) Add(moduleKey, "模块ID不能为空且不能重复。");
            if (string.IsNullOrWhiteSpace(module.Name)) Add(moduleKey, "模块名称不能为空。");
            if (!string.IsNullOrWhiteSpace(module.CoverUrl))
            {
                var coverError = assetStorage.ValidatePublishedReference(module.CoverUrl, 0, requestHost);
                if (coverError is not null) Add($"{moduleKey}.coverUrl", $"模块“{module.Name}” / 封面素材：{coverError}");
            }
            var nodeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var node in module.Nodes ?? [])
            {
                var nodeKey = $"{moduleKey}.nodes[{node.Order}]";
                if (string.IsNullOrWhiteSpace(node.Id) || !nodeIds.Add(node.Id)) Add(nodeKey, "节点ID不能为空且不能重复。");
                if (string.IsNullOrWhiteSpace(node.Name)) Add(nodeKey, "节点名称不能为空。");
                var hasPlayableVisual = (node.Assets ?? []).Any(asset =>
                    asset.Kind is AssetKind.Video or AssetKind.Animation && !string.IsNullOrWhiteSpace(asset.Url));
                if (string.IsNullOrWhiteSpace(node.NarrationText) && string.IsNullOrWhiteSpace(node.TtsAudioUrl) &&
                    node.NarrationAudio is null && !hasPlayableVisual)
                    Add(nodeKey, "讲解文案和讲解音频至少填写一项。");
                if (node.VideoVolume is < 0 or > 1) Add(nodeKey, "讲解时视频音量必须在0到1之间。");
                if (node.NarrationVolume is < 0 or > 1) Add(nodeKey, "讲解音量必须在0到1之间。");
                foreach (var asset in node.Assets ?? [])
                {
                    if (string.IsNullOrWhiteSpace(asset.Name) || string.IsNullOrWhiteSpace(asset.Url)) Add(nodeKey, "素材名称和地址不能为空。");
                    else
                    {
                        var assetError = assetStorage.ValidatePublishedReference(asset.Url, asset.SizeBytes, requestHost,
                            asset.Sha256);
                        if (assetError is not null)
                            Add($"{nodeKey}.assets[{asset.Id}]",
                                $"模块“{module.Name}” / 节点“{node.Name}” / 素材“{asset.Name}”：{assetError}");
                    }
                }
            }
        }

        var publishReadiness = ContentPublishPolicy.Evaluate(modules, assetStorage, requestHost,
            currentContent, legacyValidation);
        foreach (var issue in publishReadiness.Issues.Where(item =>
                     item.Severity == ContentPublishIssueSeverity.Error))
            Add($"modules[{issue.ModuleId}].nodes[{issue.NodeId}].narrationAudio", issue.Message);

        return errors.ToDictionary(item => item.Key, item => item.Value.ToArray());
    }
}
