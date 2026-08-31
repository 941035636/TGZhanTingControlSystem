using System.Buffers;
using System.Text;
using Microsoft.Extensions.Options;
using TG.Control.Contracts;

namespace TG.Control.Server;

public sealed class TtsMediaValidationException(string errorCode, string message) : Exception(message)
{
    public string ErrorCode { get; } = errorCode;
}

public sealed record ValidatedTtsAudio(string FilePath, string MediaType, double DurationSeconds);

public sealed class TtsMediaValidator
{
    private readonly TtsProductionOptions options;
    private readonly string stagingDirectory;

    public TtsMediaValidator(IOptions<TtsProductionOptions> options, IOptions<StorageOptions> storage,
        IHostEnvironment environment)
    {
        this.options = options.Value;
        var dataDirectory = Path.GetFullPath(storage.Value.DataDirectory, environment.ContentRootPath);
        stagingDirectory = Path.Combine(dataDirectory, "TtsStaging");
        Directory.CreateDirectory(stagingDirectory);
    }

    public async Task<ValidatedTtsAudio> ValidateAsync(Stream source, string declaredMediaType,
        TtsSynthesisConfiguration configuration, CancellationToken cancellationToken)
    {
        if (source is null) throw new TtsMediaValidationException("empty_result", "The provider returned no audio stream.");
        var mediaType = declaredMediaType?.Split(';', 2)[0].Trim().ToLowerInvariant();
        if (mediaType is not ("audio/wav" or "audio/x-wav"))
            throw new TtsMediaValidationException("unsupported_media_type",
                $"Phase 9B accepts PCM WAV only; provider declared '{declaredMediaType}'.");
        if (!string.Equals(configuration.OutputMediaType, "audio/wav", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(configuration.OutputMediaType, "audio/x-wav", StringComparison.OrdinalIgnoreCase))
            throw new TtsMediaValidationException("configuration_media_mismatch",
                "The synthesis configuration must request PCM WAV in Phase 9B.");

        var path = Path.Combine(stagingDirectory, $"{Guid.NewGuid():N}.wav.tmp");
        var buffer = ArrayPool<byte>.Shared.Rent(128 * 1024);
        long size = 0;
        try
        {
            await using (var target = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             buffer.Length, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                while (true)
                {
                    var read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                    if (read == 0) break;
                    size += read;
                    if (size > options.MaxAudioSizeBytes)
                        throw new TtsMediaValidationException("audio_too_large", "Generated audio exceeds the configured size limit.");
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
            }

            if (size < Math.Max(45, options.MinAudioSizeBytes))
                throw new TtsMediaValidationException("audio_too_small", "Generated audio is empty or too small to be valid.");

            var duration = ReadPcmWaveDuration(path, configuration);
            return new ValidatedTtsAudio(path, "audio/wav", duration);
        }
        catch
        {
            if (File.Exists(path)) File.Delete(path);
            throw;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static double ReadPcmWaveDuration(string path, TtsSynthesisConfiguration configuration)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream, Encoding.ASCII, true);
        if (ReadFourCc(reader) != "RIFF") throw InvalidWave("missing_riff", "Generated file is not a RIFF container.");
        var declaredSize = reader.ReadUInt32();
        if (ReadFourCc(reader) != "WAVE") throw InvalidWave("missing_wave", "Generated RIFF file is not WAVE audio.");
        if (declaredSize + 8 != stream.Length) throw InvalidWave("invalid_riff_size", "WAV container size is inconsistent.");

        ushort format = 0;
        ushort channels = 0;
        uint sampleRate = 0;
        uint byteRate = 0;
        ushort blockAlign = 0;
        ushort bitsPerSample = 0;
        uint dataSize = 0;
        var foundFormat = false;
        var foundData = false;

        while (stream.Position + 8 <= stream.Length)
        {
            var chunkId = ReadFourCc(reader);
            var chunkSize = reader.ReadUInt32();
            var chunkEnd = checked(stream.Position + chunkSize);
            if (chunkEnd > stream.Length) throw InvalidWave("invalid_chunk_size", "WAV chunk exceeds the file boundary.");

            if (chunkId == "fmt ")
            {
                if (chunkSize < 16) throw InvalidWave("invalid_format_chunk", "WAV format chunk is incomplete.");
                format = reader.ReadUInt16();
                channels = reader.ReadUInt16();
                sampleRate = reader.ReadUInt32();
                byteRate = reader.ReadUInt32();
                blockAlign = reader.ReadUInt16();
                bitsPerSample = reader.ReadUInt16();
                foundFormat = true;
            }
            else if (chunkId == "data")
            {
                dataSize = chunkSize;
                foundData = true;
            }

            stream.Position = chunkEnd + (chunkSize % 2);
        }

        if (!foundFormat || !foundData || dataSize == 0)
            throw InvalidWave("missing_audio_chunks", "WAV format or audio data chunk is missing.");
        if (format != 1) throw InvalidWave("unsupported_wave_codec", "Phase 9B accepts uncompressed PCM WAV only.");
        if (channels is < 1 or > 2) throw InvalidWave("unsupported_channels", "PCM WAV must be mono or stereo.");
        if (sampleRate is < 8000 or > 48000) throw InvalidWave("unsupported_sample_rate", "PCM WAV sample rate is unsupported.");
        if (bitsPerSample != 16) throw InvalidWave("unsupported_bit_depth", "Phase 9B accepts 16-bit PCM WAV only.");
        var expectedBlockAlign = channels * bitsPerSample / 8;
        if (blockAlign != expectedBlockAlign || byteRate != sampleRate * blockAlign)
            throw InvalidWave("invalid_wave_format", "PCM WAV byte rate or block alignment is inconsistent.");
        if (configuration.SampleRateHz > 0 && configuration.SampleRateHz != sampleRate)
            throw InvalidWave("sample_rate_mismatch", "Generated sample rate does not match the synthesis configuration.");
        if (configuration.Channels > 0 && configuration.Channels != channels)
            throw InvalidWave("channel_mismatch", "Generated channel count does not match the synthesis configuration.");

        var duration = dataSize / (double)byteRate;
        if (!double.IsFinite(duration) || duration <= 0)
            throw InvalidWave("invalid_duration", "Generated audio duration is invalid.");
        return duration;
    }

    private static string ReadFourCc(BinaryReader reader) => Encoding.ASCII.GetString(reader.ReadBytes(4));
    private static TtsMediaValidationException InvalidWave(string code, string message) => new(code, message);
}
