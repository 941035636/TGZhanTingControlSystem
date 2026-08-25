using TG.Control.Contracts;

namespace TG.Control.Server;

public static class DefaultModules
{
    private static readonly string[] Names =
    [
        "一路向新", "历史沿革", "不曾忘却的记忆", "子分公司", "领导关怀", "企业荣誉",
        "科技创新", "产品体系", "数智领航", "绿色低碳", "敢当石特", "党建赋能"
    ];

    public static IReadOnlyList<ExhibitionModule> Create() => Names
        .Select((name, index) => new ExhibitionModule(
            $"module-{index + 1:00}", name, index + 1, string.Empty, null, true, Array.Empty<NarrationNode>()))
        .ToArray();
}
