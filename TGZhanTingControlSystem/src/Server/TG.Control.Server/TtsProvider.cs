using TG.Control.Contracts;

namespace TG.Control.Server;

public enum TtsProviderFailureKind
{
    Transient,
    Permanent
}

public sealed class TtsProviderException : Exception
{
    public TtsProviderException(TtsProviderFailureKind failureKind, string errorCode, string message,
        Exception? innerException = null) : base(message, innerException)
    {
        FailureKind = failureKind;
        ErrorCode = errorCode;
    }

    public TtsProviderFailureKind FailureKind { get; }
    public string ErrorCode { get; }
}

public sealed record TtsProviderSynthesisRequest(
    string NarrationText,
    string NarrationTextFingerprint,
    TtsSynthesisConfiguration Configuration,
    string SynthesisConfigurationFingerprint);

public sealed record TtsProviderAudioResult(
    Stream AudioStream,
    string MediaType,
    string? ProviderRequestId = null);

public interface ITtsProvider
{
    string ProviderId { get; }
    Task<TtsProviderDescriptor> GetDescriptorAsync(CancellationToken cancellationToken);
    Task<TtsProviderAudioResult> SynthesizeAsync(TtsProviderSynthesisRequest request,
        CancellationToken cancellationToken);
}

public sealed class TtsProviderRegistry
{
    private readonly IReadOnlyDictionary<string, ITtsProvider> providers;

    public TtsProviderRegistry(IEnumerable<ITtsProvider> providers)
    {
        var values = new Dictionary<string, ITtsProvider>(StringComparer.OrdinalIgnoreCase);
        foreach (var provider in providers)
        {
            if (string.IsNullOrWhiteSpace(provider.ProviderId))
                throw new InvalidOperationException("TTS provider ID cannot be empty.");
            if (!values.TryAdd(provider.ProviderId, provider))
                throw new InvalidOperationException($"Duplicate TTS provider ID: {provider.ProviderId}");
        }
        this.providers = values;
    }

    public bool TryResolve(string providerId, out ITtsProvider provider) =>
        providers.TryGetValue(providerId, out provider!);

    public async Task<IReadOnlyList<TtsProviderDescriptor>> GetDescriptorsAsync(CancellationToken cancellationToken)
    {
        var result = new List<TtsProviderDescriptor>(providers.Count);
        foreach (var provider in providers.Values.OrderBy(item => item.ProviderId, StringComparer.OrdinalIgnoreCase))
            result.Add(await provider.GetDescriptorAsync(cancellationToken));
        return result;
    }
}
