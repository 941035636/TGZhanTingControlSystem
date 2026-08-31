using Microsoft.AspNetCore.Http;
using TG.Control.Contracts;

namespace TG.Control.Server;

public sealed record NarrationAudioBindingEvaluation(NarrationAudioBindingStatus Status, string Message);

public static class NarrationAudioBindingInspector
{
    public static NarrationAudioBindingEvaluation Evaluate(NarrationNode node, AssetStorage assetStorage,
        HostString requestHost)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(assetStorage);

        var binding = node.NarrationAudio;
        if (binding is null)
        {
            return string.IsNullOrWhiteSpace(node.TtsAudioUrl)
                ? Result(NarrationAudioBindingStatus.Missing, "尚未绑定讲解音频。")
                : Result(NarrationAudioBindingStatus.LegacyUnverified, "历史讲解音频缺少文案和完整性绑定，不能判定为有效新绑定。");
        }

        if (!HasCompleteAssetIdentity(binding.Asset, out var assetError))
            return Result(NarrationAudioBindingStatus.InvalidAsset, assetError);

        var storedAssetError = assetStorage.ValidatePublishedReference(binding.Asset.Url, binding.Asset.SizeBytes,
            requestHost, binding.Asset.Sha256);
        if (storedAssetError is not null)
            return Result(NarrationAudioBindingStatus.InvalidAsset, storedAssetError);

        if (!string.Equals(binding.FingerprintVersion, NarrationAudioFingerprint.Version, StringComparison.Ordinal) ||
            !NarrationAudioFingerprint.IsSha256(binding.NarrationTextFingerprint) ||
            !NarrationAudioFingerprint.IsSha256(binding.SynthesisConfigurationFingerprint) ||
            !NarrationAudioFingerprint.IsSynthesisConfigurationValid(binding.SynthesisConfiguration) ||
            !NarrationAudioFingerprint.IsSynthesisConfigurationValid(node.TtsConfiguration))
        {
            return Result(NarrationAudioBindingStatus.InvalidBinding, "讲解音频绑定缺少有效的指纹版本或合成配置。");
        }

        var currentTextFingerprint = NarrationAudioFingerprint.ComputeText(node.NarrationText);
        if (!string.Equals(binding.NarrationTextFingerprint, currentTextFingerprint, StringComparison.OrdinalIgnoreCase))
            return Result(NarrationAudioBindingStatus.StaleText, "讲解词已修改，当前讲解音频已过期。");

        var boundConfigurationFingerprint = NarrationAudioFingerprint.ComputeSynthesisConfiguration(binding.SynthesisConfiguration);
        if (!string.Equals(binding.SynthesisConfigurationFingerprint, boundConfigurationFingerprint,
                StringComparison.OrdinalIgnoreCase))
        {
            return Result(NarrationAudioBindingStatus.InvalidBinding, "讲解音频记录的合成配置与配置指纹不一致。");
        }

        var currentConfigurationFingerprint = NarrationAudioFingerprint.ComputeSynthesisConfiguration(node.TtsConfiguration!);
        if (!string.Equals(binding.SynthesisConfigurationFingerprint, currentConfigurationFingerprint,
                StringComparison.OrdinalIgnoreCase))
        {
            return Result(NarrationAudioBindingStatus.StaleSynthesisConfiguration, "音色或合成参数已修改，当前讲解音频已过期。");
        }

        return Result(NarrationAudioBindingStatus.Fresh, "讲解音频与当前讲解词及合成配置一致。");
    }

    public static bool HasCompleteAssetIdentity(ContentAsset? asset, out string error)
    {
        if (asset is null)
        {
            error = "讲解音频资产不存在。";
            return false;
        }
        if (string.IsNullOrWhiteSpace(asset.Id) || string.IsNullOrWhiteSpace(asset.Url))
        {
            error = "讲解音频缺少资产ID或URL。";
            return false;
        }
        if (asset.Kind != AssetKind.NarrationAudio)
        {
            error = "讲解音频绑定的资产类型无效。";
            return false;
        }
        if (!NarrationAudioFingerprint.IsSha256(asset.Sha256))
        {
            error = "讲解音频缺少有效SHA-256。";
            return false;
        }
        if (asset.SizeBytes <= 0)
        {
            error = "讲解音频缺少有效文件大小。";
            return false;
        }
        if (string.IsNullOrWhiteSpace(asset.MediaType) ||
            !asset.MediaType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase))
        {
            error = "讲解音频缺少有效媒体格式。";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static NarrationAudioBindingEvaluation Result(NarrationAudioBindingStatus status, string message) =>
        new(status, message);
}
