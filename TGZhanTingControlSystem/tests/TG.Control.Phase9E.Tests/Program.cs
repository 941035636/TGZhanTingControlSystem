using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TG.Control.Contracts;
using TG.Control.Server;

namespace TG.Control.Phase9E.Tests;

internal static class Program
{
    private static async Task<int> Main()
    {
        var tests = new (string Name, Func<Task> Run)[]
        {
            ("Missing runtime is reported without registering a fake voice", MissingRuntimeIsUnavailable),
            ("Provider capabilities expose only the real Chinese voice", DescriptorMapsWorkerCapabilities),
            ("Provider streams Worker WAV without trusting integrity metadata", SynthesisStreamsWorkerWav),
            ("Worker permanent error remains a permanent Provider error", PermanentFailureIsMapped),
            ("Cancellation reaches synthesis and sends best-effort cancel", CancellationIsPropagated),
            ("Non-loopback Worker address is rejected", NonLoopbackAddressIsRejected)
        };

        var failures = 0;
        foreach (var test in tests)
        {
            try
            {
                await test.Run();
                Console.WriteLine($"PASS  {test.Name}");
            }
            catch (Exception exception)
            {
                failures++;
                Console.Error.WriteLine($"FAIL  {test.Name}: {exception}");
            }
        }
        Console.WriteLine($"Phase 9E tests: {tests.Length - failures}/{tests.Length} passed.");
        return failures == 0 ? 0 : 1;
    }

    private static async Task MissingRuntimeIsUnavailable()
    {
        await using var context = ProviderContext.Create(validLayout: false, _ => Json(HttpStatusCode.OK, "{}"));
        var descriptor = await context.Provider.GetDescriptorAsync(CancellationToken.None);
        False(descriptor.Available);
        Equal(0, descriptor.Voices.Count);
        Contains("运行时", descriptor.UnavailableReason);
    }

    private static async Task DescriptorMapsWorkerCapabilities()
    {
        await using var context = ProviderContext.Create(validLayout: true, request => request.RequestUri?.AbsolutePath switch
        {
            "/health" => Json(HttpStatusCode.OK, """{"available":true,"message":null}"""),
            "/voices" => Json(HttpStatusCode.OK,
                """{"voices":[{"voiceId":"zh-standard","displayName":"中文标准讲解","language":"zh-CN"}],"capabilities":{"maxTextLength":5000,"minRate":0.75,"maxRate":1.25,"minPitch":0,"maxPitch":0,"supportedMediaTypes":["audio/wav"],"supportsRate":true,"supportsPitch":false,"supportsVolume":false,"defaultSampleRateHz":44100,"defaultChannels":1,"fixedSampleRateHz":44100,"fixedChannels":1}}"""),
            _ => Json(HttpStatusCode.NotFound, "{}")
        });
        var descriptor = await context.Provider.GetDescriptorAsync(CancellationToken.None);
        True(descriptor.Available);
        False(descriptor.DevelopmentOnly);
        Equal("zh-standard", descriptor.Voices.Single().VoiceId);
        False(descriptor.Capabilities.SupportsPitch);
        False(descriptor.Capabilities.SupportsVolume);
        Equal(44100, descriptor.Capabilities.DefaultSampleRateHz);
        Equal(1, descriptor.Capabilities.DefaultChannels);
        Equal(44100, descriptor.Capabilities.FixedSampleRateHz);
        Equal(1, descriptor.Capabilities.FixedChannels);
    }

    private static async Task SynthesisStreamsWorkerWav()
    {
        var wave = WaveBytes();
        await using var context = ProviderContext.Create(validLayout: true, request =>
        {
            if (request.RequestUri?.AbsolutePath != "/synthesize") return Json(HttpStatusCode.NotFound, "{}");
            if (request.Content?.Headers.ContentLength is null)
                throw new InvalidOperationException("Provider request must have a deterministic Content-Length.");
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(wave) };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
            response.Headers.Add("X-TG-TTS-Request-Id", "worker-request-1");
            return response;
        });
        var result = await context.Provider.SynthesizeAsync(Request(), CancellationToken.None);
        await using var stream = result.AudioStream;
        using var copy = new MemoryStream();
        await stream.CopyToAsync(copy);
        Equal("audio/wav", result.MediaType);
        Equal("worker-request-1", result.ProviderRequestId);
        SequenceEqual(wave, copy.ToArray());
    }

    private static async Task PermanentFailureIsMapped()
    {
        await using var context = ProviderContext.Create(validLayout: true, request =>
            request.RequestUri?.AbsolutePath == "/synthesize"
                ? Json(HttpStatusCode.BadRequest,
                    """{"error":{"code":"invalid_voice","message":"voice rejected","transient":false}}""")
                : Json(HttpStatusCode.NotFound, "{}"));
        try
        {
            _ = await context.Provider.SynthesizeAsync(Request(), CancellationToken.None);
            throw new InvalidOperationException("Expected TtsProviderException.");
        }
        catch (TtsProviderException exception)
        {
            Equal(TtsProviderFailureKind.Permanent, exception.FailureKind);
            Equal("invalid_voice", exception.ErrorCode);
        }
    }

    private static async Task CancellationIsPropagated()
    {
        var cancelPosted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var context = ProviderContext.Create(validLayout: true, async (request, cancellationToken) =>
        {
            if (request.RequestUri?.AbsolutePath.EndsWith("/cancel", StringComparison.Ordinal) == true)
            {
                cancelPosted.TrySetResult();
                return Json(HttpStatusCode.OK, """{"cancelled":true}""");
            }
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return Json(HttpStatusCode.OK, "{}");
        });
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        await ThrowsAsync<OperationCanceledException>(() =>
            context.Provider.SynthesizeAsync(Request(), cancellation.Token));
        await cancelPosted.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    private static async Task NonLoopbackAddressIsRejected()
    {
        await using var context = ProviderContext.Create(validLayout: true, _ => Json(HttpStatusCode.OK, "{}"),
            "http://192.0.2.1:5091");
        var descriptor = await context.Provider.GetDescriptorAsync(CancellationToken.None);
        False(descriptor.Available);
        Contains("loopback", descriptor.UnavailableReason);
    }

    private static TtsProviderSynthesisRequest Request()
    {
        var configuration = new TtsSynthesisConfiguration(MeloTtsLocalProvider.Id, MeloTtsLocalProvider.VoiceId,
            "zh-CN", 1, 0, 1, "audio/wav", 44100, 1);
        return new TtsProviderSynthesisRequest("欢迎来到智慧展厅。", "text", configuration, "config");
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private static byte[] WaveBytes()
    {
        var bytes = new byte[48];
        Encoding.ASCII.GetBytes("RIFF").CopyTo(bytes, 0);
        BitConverter.GetBytes(40).CopyTo(bytes, 4);
        Encoding.ASCII.GetBytes("WAVEfmt ").CopyTo(bytes, 8);
        BitConverter.GetBytes(16).CopyTo(bytes, 16);
        BitConverter.GetBytes((short)1).CopyTo(bytes, 20);
        BitConverter.GetBytes((short)1).CopyTo(bytes, 22);
        BitConverter.GetBytes(44100).CopyTo(bytes, 24);
        BitConverter.GetBytes(88200).CopyTo(bytes, 28);
        BitConverter.GetBytes((short)2).CopyTo(bytes, 32);
        BitConverter.GetBytes((short)16).CopyTo(bytes, 34);
        Encoding.ASCII.GetBytes("data").CopyTo(bytes, 36);
        BitConverter.GetBytes(4).CopyTo(bytes, 40);
        return bytes;
    }

    private static void True(bool value)
    {
        if (!value) throw new InvalidOperationException("Expected true.");
    }

    private static void False(bool value) => True(!value);

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected {expected}, got {actual}.");
    }

    private static void Contains(string expected, string? actual)
    {
        if (actual is null || !actual.Contains(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Expected '{actual}' to contain '{expected}'.");
    }

    private static void SequenceEqual(byte[] expected, byte[] actual)
    {
        if (!expected.SequenceEqual(actual)) throw new InvalidOperationException("Byte sequences differ.");
    }

    private static async Task ThrowsAsync<T>(Func<Task> action) where T : Exception
    {
        try { await action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }

    private sealed class ProviderContext : IAsyncDisposable
    {
        private readonly string directory;

        private ProviderContext(string directory, MeloTtsLocalProvider provider)
        {
            this.directory = directory;
            Provider = provider;
        }

        public MeloTtsLocalProvider Provider { get; }

        public static ProviderContext Create(bool validLayout, Func<HttpRequestMessage, HttpResponseMessage> handler,
            string baseAddress = "http://127.0.0.1:5091") =>
            Create(validLayout, (request, _) => Task.FromResult(handler(request)), baseAddress);

        public static ProviderContext Create(bool validLayout,
            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler,
            string baseAddress = "http://127.0.0.1:5091")
        {
            var directory = Path.Combine(Path.GetTempPath(), "TG-Phase9E-Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var options = new MeloTtsLocalOptions
            {
                Enabled = true,
                AutoStartWorker = false,
                BaseAddress = baseAddress,
                PythonExecutablePath = Path.Combine(directory, "runtime", "python.exe"),
                WorkerScriptPath = Path.Combine(directory, "worker.py"),
                MeloTtsSourcePath = Path.Combine(directory, "vendor", "MeloTTS"),
                AcousticModelPath = Path.Combine(directory, "models", "MeloTTS-Chinese"),
                BertModelPath = Path.Combine(directory, "models", "bert"),
                NltkDataPath = Path.Combine(directory, "runtime", "nltk_data"),
                HealthTimeoutMilliseconds = 500
            };
            if (validLayout) CreateLayout(options);
            var wrapped = Options.Create(options);
            var supervisor = new MeloTtsWorkerSupervisor(wrapped,
                NullLogger<MeloTtsWorkerSupervisor>.Instance);
            var provider = new MeloTtsLocalProvider(new FakeHttpClientFactory(handler), wrapped, supervisor,
                NullLogger<MeloTtsLocalProvider>.Instance);
            return new ProviderContext(directory, provider);
        }

        private static void CreateLayout(MeloTtsLocalOptions options)
        {
            foreach (var directory in new[]
                     {
                         Path.GetDirectoryName(options.PythonExecutablePath)!,
                         Path.GetDirectoryName(options.WorkerScriptPath)!,
                         Path.Combine(options.MeloTtsSourcePath, "melo"), options.AcousticModelPath,
                         options.BertModelPath, options.NltkDataPath
                     })
                Directory.CreateDirectory(directory);
            File.WriteAllText(options.PythonExecutablePath, "test");
            File.WriteAllText(options.WorkerScriptPath, "test");
            File.WriteAllText(Path.Combine(options.MeloTtsSourcePath, "melo", "api.py"), "test");
            File.WriteAllText(Path.Combine(options.AcousticModelPath, "config.json"), "test");
            File.WriteAllText(Path.Combine(options.AcousticModelPath, "checkpoint.pth"), "test");
            File.WriteAllText(Path.Combine(options.BertModelPath, "config.json"), "test");
            File.WriteAllText(Path.Combine(options.BertModelPath, "pytorch_model.bin"), "test");
            File.WriteAllText(Path.Combine(options.BertModelPath, "vocab.txt"), "test");
        }

        public ValueTask DisposeAsync()
        {
            Directory.Delete(directory, true);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeHttpClientFactory(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new FakeHandler(handler));
    }

    private sealed class FakeHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) => handler(request, cancellationToken);
    }
}
