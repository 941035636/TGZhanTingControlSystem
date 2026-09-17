using System.Text.RegularExpressions;
using TG.Control.Contracts;

namespace TG.Control.Server;

/// <summary>Server-side guardrail for declarative terminal UI configuration.</summary>
public static class UiExperiencePolicy
{
    private static readonly Regex HexColor = new("^#[0-9a-fA-F]{6}$", RegexOptions.Compiled);
    private static readonly HashSet<string> TouchKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "shell.title", "shell.subtitle", "home.hero.title", "home.hero.subtitle",
        "home.hero.logo", "home.hero.background", "home.quick.temporary", "home.quick.all",
        "home.quick.continue"
    };
    private static readonly HashSet<string> LedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "idle.title", "idle.subtitle", "idle.logo", "idle.background", "idle.status"
    };

    public static UiExperienceConfig Normalize(UiExperienceConfig request)
    {
        var layout = request.Layout ?? new UiExperienceLayout();
        return request with
        {
            Layout = new UiExperienceLayout(
                NormalizeTemplate(layout.TouchTemplate, "hero-routes", "hero-routes", "routes-grid"),
                NormalizeTemplate(layout.LedTemplate, "idle-media", "idle-media", "idle-minimal"),
                layout.TouchShowHero,
                layout.TouchShowStatusPanel,
                layout.TouchShowQuickActions,
                layout.LedShowBranding,
                layout.LedShowStatus),
            TouchElements = NormalizeElements(request.TouchElements, TouchKeys),
            LedElements = NormalizeElements(request.LedElements, LedKeys)
        };
    }

    public static IReadOnlyDictionary<string, string[]> Validate(UiExperienceConfig config)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        ValidateElements(config.TouchElements, TouchKeys, "touchElements", errors);
        ValidateElements(config.LedElements, LedKeys, "ledElements", errors);
        return errors;
    }

    private static UiElementOverride[] NormalizeElements(IReadOnlyList<UiElementOverride>? values,
        HashSet<string> allowed)
    {
        if (values is null || values.Count == 0) return Array.Empty<UiElementOverride>();
        return values
            .Where(item => item is not null && allowed.Contains(item.Key ?? string.Empty))
            .GroupBy(item => item.Key.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last() with
            {
                Key = group.Key,
                Text = CleanText(group.Last().Text),
                Color = NormalizeColor(group.Last().Color),
                AssetUrl = CleanText(group.Last().AssetUrl),
                AssetId = CleanText(group.Last().AssetId),
                AssetSha256 = CleanText(group.Last().AssetSha256),
                AssetMediaType = CleanText(group.Last().AssetMediaType),
                AssetSizeBytes = Math.Max(0, group.Last().AssetSizeBytes)
            })
            .ToArray();
    }

    private static void ValidateElements(IReadOnlyList<UiElementOverride>? values, HashSet<string> allowed,
        string path, IDictionary<string, string[]> errors)
    {
        if (values is null) return;
        var invalid = values.Where(item => item is null || string.IsNullOrWhiteSpace(item.Key) || !allowed.Contains(item.Key))
            .Select(item => item?.Key ?? "(empty)").Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (invalid.Length > 0) errors[path] = [$"包含不支持的界面元素：{string.Join("、", invalid)}"];
        foreach (var item in values.Where(item => item is not null && allowed.Contains(item.Key)))
        {
            if (item.Text?.Length > 200) errors[$"{path}.{item.Key}.text"] = ["界面文字不能超过200个字符。"];
            if (!string.IsNullOrWhiteSpace(item.Color) && !HexColor.IsMatch(item.Color))
                errors[$"{path}.{item.Key}.color"] = ["颜色必须使用#RRGGBB格式。"];
            if ((!string.IsNullOrWhiteSpace(item.AssetId) || !string.IsNullOrWhiteSpace(item.AssetUrl)) &&
                (string.IsNullOrWhiteSpace(item.AssetId) || string.IsNullOrWhiteSpace(item.AssetUrl) ||
                 string.IsNullOrWhiteSpace(item.AssetSha256) || item.AssetSha256.Length != 64 || item.AssetSizeBytes <= 0))
                errors[$"{path}.{item.Key}.asset"] = ["界面素材缺少完整的AssetId、SHA-256或文件大小。"];
        }
    }

    private static string NormalizeTemplate(string? value, string fallback, params string[] allowed) =>
        allowed.Contains(value ?? string.Empty, StringComparer.OrdinalIgnoreCase) ? value! : fallback;

    private static string? CleanText(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string? NormalizeColor(string? value) => !string.IsNullOrWhiteSpace(value) && HexColor.IsMatch(value) ? value : null;
}
