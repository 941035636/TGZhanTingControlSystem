using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TG.Control.Contracts;

namespace TG.Control.Server;

public sealed class MeloTtsLocalProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<MeloTtsLocalOptions> configuredOptions,
    MeloTtsWorkerSupervisor supervisor,
    ILogger<MeloTtsLocalProvider> logger) : ITtsProvider
{
    public const string Id = "melo-local";
    public const string VoiceId = "zh-standard";
    public const string HttpClientName = "melo-tts-local-worker";
    private static readonly TtsProviderCapabilities UnavailableCapabilities = new(
        5000, .75, 1.25, 0, 0, ["audio/wav"],
        SupportsRate: true, SupportsPitch: false, SupportsVolume: false,
        DefaultSampleRateHz: 44100, DefaultChannels: 1,
        FixedSampleRateHz: 44100, FixedChannels: 1);
    private static readonly JsonSerializerOptions WorkerJson = new(JsonSerializerDefaults.Web);
    private readonly MeloTtsLocalOptions options = configuredOptions.Value;

    public string ProviderId => Id;

    public async Task<TtsProviderDescriptor> GetDescriptorAsync(CancellationToken cancellationToken)
    {
        var localError = supervisor.GetConfigurationError();
        if (localError is not null) return Unavailable(localError);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMilliseconds(Math.Max(250, options.HealthTimeoutMilliseconds)));
            using var client = CreateClient();
            var health = await client.GetFromJsonAsync<WorkerHealthResponse>("health", timeout.Token);
            if (health is null || !health.Available)
                return Unavailable(health?.Message ?? "MeloTTS 本地语音服务尚未就绪。");
            var voices = await client.GetFromJsonAsync<WorkerVoicesResponse>("voices", timeout.Token);
            if (voices is null || voices.Voices.Count == 0)
                return Unavailable("MeloTTS 本地语音服务没有可用中文音色。");
            return new TtsProviderDescriptor(Id, "MeloTTS 本地中文", true, false, null,
                voices.Voices.Select(item => new TtsVoiceDescriptor(item.VoiceId, item.DisplayName, item.Language)).ToArray(),
                new TtsProviderCapabilities(voices.Capabilities.MaxTextLength,
                    voices.Capabilities.MinRate, voices.Capabilities.MaxRate,
                    voices.Capabilities.MinPitch, voices.Capabilities.MaxPitch,
                    voices.Capabilities.SupportedMediaTypes,
                    voices.Capabilities.SupportsRate, voices.Capabilities.SupportsPitch,
                    voices.Capabilities.SupportsVolume, voices.Capabilities.DefaultSampleRateHz,
                    voices.Capabilities.DefaultChannels, voices.Capabilities.FixedSampleRateHz,
                    voices.Capabilities.FixedChannels));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Unavailable("MeloTTS 本地语音服务启动中或响应超时。");
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or NotSupportedException)
        {
            logger.LogDebug(exception, "MeloTTS local Worker health query failed.");
            return Unavailable(supervisor.GetRuntimeError() ?? "MeloTTS 本地语音服务未连接。");
        }
    }

    public async Task<TtsProviderAudioResult> SynthesizeAsync(TtsProviderSynthesisRequest request,
        CancellationToken cancellationToken)
    {
        var requestId = $"melo-{Guid.NewGuid():N}";
        using var registration = cancellationToken.Register(() => _ = CancelBestEffortAsync(requestId));
        try
        {
            using var client = CreateClient();
            var payload = JsonSerializer.SerializeToUtf8Bytes(new WorkerSynthesisRequest(requestId,
                request.NarrationText, request.Configuration.Voice, request.Configuration.Language,
                request.Configuration.Rate, request.Configuration.Pitch, request.Configuration.Volume,
                request.Configuration.OutputMediaType, request.Configuration.SampleRateHz,
                request.Configuration.Channels), WorkerJson);
            var content = new ByteArrayContent(payload);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
            using var message = new HttpRequestMessage(HttpMethod.Post, "synthesize")
            {
                Content = content
            };
            var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var failure = await ReadFailureAsync(response, cancellationToken);
                response.Dispose();
                throw new TtsProviderException(failure.Transient
                        ? TtsProviderFailureKind.Transient
                        : TtsProviderFailureKind.Permanent,
                    failure.Code, failure.Message);
            }
            var mediaType = response.Content.Headers.ContentType?.MediaType ?? "audio/wav";
            var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return new TtsProviderAudioResult(new ResponseOwnedStream(responseStream, response), mediaType,
                response.Headers.TryGetValues("X-TG-TTS-Request-Id", out var values)
                    ? values.FirstOrDefault() ?? requestId
                    : requestId);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (TtsProviderException)
        {
            throw;
        }
        catch (HttpRequestException exception)
        {
            throw new TtsProviderException(TtsProviderFailureKind.Transient, "worker_unavailable",
                "MeloTTS 本地语音服务连接失败。", exception);
        }
        catch (JsonException exception)
        {
            throw new TtsProviderException(TtsProviderFailureKind.Transient, "worker_invalid_response",
                "MeloTTS 本地语音服务返回了无法识别的结果。", exception);
        }
    }

    private HttpClient CreateClient()
    {
        if (!Uri.TryCreate(options.BaseAddress.TrimEnd('/') + "/", UriKind.Absolute, out var baseAddress) ||
            !baseAddress.IsLoopback)
            throw new InvalidOperationException("MeloTTS Worker address must be loopback.");
        var client = httpClientFactory.CreateClient(HttpClientName);
        client.BaseAddress = baseAddress;
        client.Timeout = Timeout.InfiniteTimeSpan;
        return client;
    }

    private async Task CancelBestEffortAsync(string requestId)
    {
        try
        {
            using var client = CreateClient();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            using var response = await client.PostAsync($"requests/{Uri.EscapeDataString(requestId)}/cancel", null,
                timeout.Token);
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
        {
            logger.LogDebug(exception, "Could not notify MeloTTS Worker to cancel request {RequestId}.", requestId);
        }
    }

    private static async Task<WorkerError> ReadFailureAsync(HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            var body = await response.Content.ReadFromJsonAsync<WorkerErrorResponse>(cancellationToken: cancellationToken);
            if (body?.Error is not null) return body.Error;
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException) { }
        return new WorkerError("worker_failure", "MeloTTS 本地语音生成失败。",
            response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable or
                HttpStatusCode.GatewayTimeout);
    }

    private static TtsProviderDescriptor Unavailable(string reason) => new(Id, "MeloTTS 本地中文", false, false,
        reason, [], UnavailableCapabilities);

    private sealed record WorkerHealthResponse(bool Available, string? Message);
    private sealed record WorkerVoicesResponse(IReadOnlyList<WorkerVoice> Voices, WorkerCapabilities Capabilities);
    private sealed record WorkerVoice(string VoiceId, string DisplayName, string Language);
    private sealed record WorkerCapabilities(int MaxTextLength, double MinRate, double MaxRate, double MinPitch,
        double MaxPitch, IReadOnlyList<string> SupportedMediaTypes, bool SupportsRate, bool SupportsPitch,
        bool SupportsVolume, int DefaultSampleRateHz, int DefaultChannels, int FixedSampleRateHz, int FixedChannels);
    private sealed record WorkerSynthesisRequest(string RequestId, string Text, string Voice, string Language,
        double Rate, double Pitch, double Volume, string OutputMediaType, int SampleRateHz, int Channels);
    private sealed record WorkerErrorResponse(WorkerError Error);
    private sealed record WorkerError(string Code, string Message, bool Transient);

    private sealed class ResponseOwnedStream(Stream inner, HttpResponseMessage response) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            inner.ReadAsync(buffer, cancellationToken);
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
                response.Dispose();
            }
            base.Dispose(disposing);
        }
        public override async ValueTask DisposeAsync()
        {
            await inner.DisposeAsync();
            response.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
