using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TG.Control.Contracts;

namespace TG.Control.Server;

public static class NarrationAudioFingerprint
{
    public const string Version = "tts-binding-v1";
    private const string TextDomain = "tg:narration-text:v1";
    private const string ConfigurationDomain = "tg:tts-synthesis-configuration:v1";

    public static string ComputeText(string? narrationText)
    {
        var normalized = NormalizeText(narrationText);
        return ComputeSha256(TextDomain + "\n" + normalized);
    }

    public static string ComputeSynthesisConfiguration(TtsSynthesisConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (!IsSynthesisConfigurationValid(configuration))
            throw new ArgumentException("TTS synthesis configuration is invalid.", nameof(configuration));

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("schema", ConfigurationDomain);
            writer.WriteString("providerKey", NormalizeKey(configuration.ProviderKey));
            writer.WriteString("voice", NormalizeValue(configuration.Voice));
            writer.WriteString("language", NormalizeKey(configuration.Language));
            writer.WriteNumber("rate", configuration.Rate);
            writer.WriteNumber("pitch", configuration.Pitch);
            writer.WriteNumber("volume", configuration.Volume);
            writer.WriteString("outputMediaType", NormalizeKey(configuration.OutputMediaType));
            writer.WriteNumber("sampleRateHz", configuration.SampleRateHz);
            writer.WriteNumber("channels", configuration.Channels);
            writer.WriteEndObject();
        }
        return ComputeSha256(stream.ToArray());
    }

    public static bool IsSynthesisConfigurationValid(TtsSynthesisConfiguration? configuration) =>
        configuration is not null &&
        !string.IsNullOrWhiteSpace(configuration.ProviderKey) &&
        !string.IsNullOrWhiteSpace(configuration.Voice) &&
        !string.IsNullOrWhiteSpace(configuration.Language) &&
        !string.IsNullOrWhiteSpace(configuration.OutputMediaType) &&
        double.IsFinite(configuration.Rate) && configuration.Rate > 0 &&
        double.IsFinite(configuration.Pitch) &&
        double.IsFinite(configuration.Volume) && configuration.Volume >= 0 &&
        configuration.SampleRateHz >= 0 &&
        configuration.Channels >= 0;

    public static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);

    internal static string NormalizeText(string? value) =>
        (value ?? string.Empty)
        .Replace("\r\n", "\n", StringComparison.Ordinal)
        .Replace('\r', '\n')
        .Normalize(NormalizationForm.FormC)
        .Trim();

    private static string NormalizeKey(string value) => NormalizeValue(value).ToLowerInvariant();

    private static string NormalizeValue(string value) => value.Trim().Normalize(NormalizationForm.FormC);

    private static string ComputeSha256(string value) => ComputeSha256(Encoding.UTF8.GetBytes(value));

    private static string ComputeSha256(byte[] value) =>
        Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();
}
