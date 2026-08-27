using TG.Control.Contracts;

namespace TG.Control.Server;

public static class ContentValidator
{
    public static Dictionary<string, string[]> Validate(IReadOnlyList<ExhibitionModule>? modules)
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
            var nodeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var node in module.Nodes ?? [])
            {
                var nodeKey = $"{moduleKey}.nodes[{node.Order}]";
                if (string.IsNullOrWhiteSpace(node.Id) || !nodeIds.Add(node.Id)) Add(nodeKey, "节点ID不能为空且不能重复。");
                if (string.IsNullOrWhiteSpace(node.Name)) Add(nodeKey, "节点名称不能为空。");
                if (string.IsNullOrWhiteSpace(node.NarrationText) && string.IsNullOrWhiteSpace(node.TtsAudioUrl))
                    Add(nodeKey, "讲解文案和讲解音频至少填写一项。");
                if (node.VideoVolume is < 0 or > 1) Add(nodeKey, "讲解时视频音量必须在0到1之间。");
                if (node.NarrationVolume is < 0 or > 1) Add(nodeKey, "讲解音量必须在0到1之间。");
                foreach (var asset in node.Assets ?? [])
                {
                    if (string.IsNullOrWhiteSpace(asset.Name) || string.IsNullOrWhiteSpace(asset.Url)) Add(nodeKey, "素材名称和地址不能为空。");
                }
            }
        }

        return errors.ToDictionary(item => item.Key, item => item.Value.ToArray());
    }
}
