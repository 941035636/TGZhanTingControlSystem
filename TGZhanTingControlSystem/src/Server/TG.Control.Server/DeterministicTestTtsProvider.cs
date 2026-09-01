using System.Security.Cryptography;
using System.Text;
using TG.Control.Contracts;

namespace TG.Control.Server;

/// <summary>
/// Development/test fixture only. It creates a deterministic PCM WAV tone and does not perform speech synthesis.
/// It is registered only when explicitly enabled in the Development environment.
/// </summary>
public sealed class DeterministicTestTtsProvider : ITtsProvider
{
    public const string Id = "deterministic-test";
    public const string VoiceId = "test-tone-zh-cn";
    public string ProviderId => Id;

    public Task<TtsProviderDescriptor> GetDescriptorAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new TtsProviderDescriptor(Id, "Deterministic test tone (TEST ONLY)", true, true,
            null,
            [new TtsVoiceDescriptor(VoiceId, "Deterministic test tone", "zh-CN")],
            new TtsProviderCapabilities(5000, 0.5, 2, -1, 1, ["audio/wav"],
                DefaultSampleRateHz: 24000, DefaultChannels: 1)));
    }

    public Task<TtsProviderAudioResult> SynthesizeAsync(TtsProviderSynthesisRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var seed = SHA256.HashData(Encoding.UTF8.GetBytes(
            request.NarrationTextFingerprint + "\n" + request.SynthesisConfigurationFingerprint));
        var sampleRate = request.Configuration.SampleRateHz == 0 ? 24000 : request.Configuration.SampleRateHz;
        var channels = (short)(request.Configuration.Channels == 0 ? 1 : request.Configuration.Channels);
        if (sampleRate is < 8000 or > 48000 || channels is < 1 or > 2)
            throw new TtsProviderException(TtsProviderFailureKind.Permanent, "unsupported_pcm_format",
                "The deterministic test provider supports 8-48 kHz mono/stereo PCM only.");
        const short bitsPerSample = 16;
        var sampleCount = sampleRate / 4 + seed[0] * 4;
        var frequency = 220 + seed[1];
        var dataSize = sampleCount * channels * (bitsPerSample / 8);
        var stream = new MemoryStream(44 + dataSize);
        using (var writer = new BinaryWriter(stream, Encoding.ASCII, true))
        {
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + dataSize);
            writer.Write(Encoding.ASCII.GetBytes("WAVE"));
            writer.Write(Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16);
            writer.Write((short)1);
            writer.Write(channels);
            writer.Write(sampleRate);
            writer.Write(sampleRate * channels * (bitsPerSample / 8));
            writer.Write((short)(channels * (bitsPerSample / 8)));
            writer.Write(bitsPerSample);
            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(dataSize);
            for (var index = 0; index < sampleCount; index++)
            {
                if ((index & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                var sample = Math.Sin(2 * Math.PI * frequency * index / sampleRate) * 0.15;
                for (var channel = 0; channel < channels; channel++)
                    writer.Write((short)(sample * short.MaxValue));
            }
        }
        stream.Position = 0;
        return Task.FromResult(new TtsProviderAudioResult(stream, "audio/wav",
            "deterministic-" + Convert.ToHexString(seed[..8]).ToLowerInvariant()));
    }
}
