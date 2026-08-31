using TG.Control.Contracts;

namespace TG.Control.Server;

public static class NarrationAudioCompatibility
{
    public static PublishedContent Normalize(PublishedContent content) =>
        content with { Modules = NormalizeModules(content.Modules) };

    public static IReadOnlyList<ExhibitionModule> NormalizeModules(IReadOnlyList<ExhibitionModule> modules) =>
        modules.Select(module => module with
        {
            Nodes = (module.Nodes ?? []).Select(NormalizeNode).ToArray()
        }).ToArray();

    public static NarrationNode NormalizeNode(NarrationNode node) =>
        node.NarrationAudio is null
            ? node
            : node with { TtsAudioUrl = node.NarrationAudio.Asset.Url };
}
