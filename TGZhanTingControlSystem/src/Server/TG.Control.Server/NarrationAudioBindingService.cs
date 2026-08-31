using Microsoft.AspNetCore.Http;
using TG.Control.Contracts;

namespace TG.Control.Server;

public sealed class NarrationAudioBindingService(AssetStorage assetStorage)
{
    public NarrationAudioBinding CreateManualBinding(CreateManualNarrationAudioBindingRequest request,
        HostString requestHost)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Asset);

        var mediaType = string.IsNullOrWhiteSpace(request.Asset.MediaType)
            ? ResolveAudioMediaType(request.Asset.Url)
            : request.Asset.MediaType.Trim().ToLowerInvariant();
        var asset = request.Asset with { MediaType = mediaType };
        if (!NarrationAudioBindingInspector.HasCompleteAssetIdentity(asset, out var identityError))
            throw new InvalidDataException(identityError);

        var storageError = assetStorage.ValidatePublishedReference(asset.Url, asset.SizeBytes, requestHost, asset.Sha256);
        if (storageError is not null) throw new InvalidDataException(storageError);

        var configuration = new TtsSynthesisConfiguration(
            "manual-upload", "manual-recording", string.IsNullOrWhiteSpace(request.Language) ? "zh-CN" : request.Language,
            1, 0, 1, mediaType, 0, 0);
        return new NarrationAudioBinding(asset,
            NarrationAudioFingerprint.ComputeText(request.NarrationText),
            NarrationAudioFingerprint.ComputeSynthesisConfiguration(configuration),
            configuration,
            NarrationAudioOrigin.ManualUpload,
            DateTimeOffset.UtcNow,
            NarrationAudioFingerprint.Version);
    }

    private static string ResolveAudioMediaType(string url) => Path.GetExtension(url.Split('?', '#')[0]).ToLowerInvariant() switch
    {
        ".mp3" => "audio/mpeg",
        ".wav" => "audio/wav",
        ".aac" => "audio/aac",
        ".m4a" => "audio/mp4",
        _ => "application/octet-stream"
    };
}
