using TG.Control.Contracts;

namespace TG.Control.Server;

public interface ITtsService
{
    Task<TtsSynthesisResult> SynthesizeAsync(TtsSynthesisRequest request, CancellationToken cancellationToken);
}

public sealed class UnconfiguredTtsService : ITtsService
{
    public Task<TtsSynthesisResult> SynthesizeAsync(TtsSynthesisRequest request, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("TTS provider is not configured. Configure a provider adapter before synthesis.");
}
