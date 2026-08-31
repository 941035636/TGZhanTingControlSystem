namespace TG.Control.Contracts;

public sealed record CreateManualNarrationAudioBindingRequest(
    ContentAsset Asset,
    string NarrationText,
    string Language = "zh-CN");
