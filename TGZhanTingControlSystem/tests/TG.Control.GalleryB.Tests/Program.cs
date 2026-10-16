using System.Text.Json;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TG.Control.Contracts;
using TG.Control.Server;

namespace TG.Control.GalleryB.Tests;

internal static class Program
{
    public static async Task<int> Main()
    {
        var tests = new (string Name, Func<Task> Execute)[]
        {
            ("start emits complete Prepare command", StartEmitsPrepare),
            ("Ready emits Show and Showing updates session", ReadyEmitsShow),
            ("stale acknowledgement cannot replace newer image", StaleReadyIsIgnored),
            ("display failure is persisted and pauses slideshow", DisplayFailureIsPersisted),
            ("previous next show and slideshow state are deterministic", ControlsAreDeterministic),
            ("formal playback and welcome protect gallery start", HigherPriorityActivityBlocksStart),
            ("higher priority transition stops gallery first", HigherPriorityTransitionStopsGallery),
            ("session persists and LED reconnect reissues current image", RestartAndReconnectRecover),
            ("incompatible published version discards recovery", VersionChangeDiscardsRecovery),
            ("invalid image integrity is rejected", InvalidAssetIsRejected),
            ("LED offline and strict version gate reject start", LedGateIsEnforced),
            ("gallery channel is isolated from playback and welcome", CommandChannelsAreIsolated),
            ("contracts keep explicit enum values and JSON round-trip", ContractsRoundTrip)
        };

        foreach (var test in tests)
        {
            try
            {
                await test.Execute();
                Console.WriteLine($"PASS {test.Name}");
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine($"FAIL {test.Name}: {exception}");
                return 1;
            }
        }
        return 0;
    }

    private static async Task StartEmitsPrepare()
    {
        using var context = await TestContext.CreateAsync();
        var response = await context.Coordinator.StartAsync(new StartGalleryRequest("module-1", 1, "tester"), default);
        var command = await context.NextGalleryAsync();
        Equal(GalleryDisplayAction.Prepare, command.Action);
        Equal(response.SessionId, command.SessionId);
        Equal(context.Images[1].Id, command.ImageId);
        Equal(context.Images[1].Sha256, command.Sha256);
        Equal(context.Images[1].SizeBytes, command.SizeBytes);
        Equal(context.Content.Version, command.ContentVersion);
        Equal(GalleryFitMode.Contain, command.FitMode);
    }

    private static async Task ReadyEmitsShow()
    {
        using var context = await TestContext.CreateAsync();
        var response = await context.Coordinator.StartAsync(new StartGalleryRequest("module-1"), default);
        var prepare = await context.NextGalleryAsync();
        await context.Coordinator.ReportAsync(new GalleryDisplayStatusReport("led-main", prepare.CommandId,
            response.SessionId, prepare.ImageId, GalleryDisplayState.Ready, null, DateTimeOffset.UtcNow), default);
        var show = await context.NextGalleryAsync();
        Equal(GalleryDisplayAction.Show, show.Action);
        True(show.ExecuteAtUtc > DateTimeOffset.UtcNow, "Show must retain a future scheduled start time.");
        await context.Coordinator.ReportAsync(new GalleryDisplayStatusReport("led-main", show.CommandId,
            response.SessionId, show.ImageId, GalleryDisplayState.Showing, null, DateTimeOffset.UtcNow), default);
        var status = await context.Coordinator.GetActiveAsync(default);
        Equal(GallerySessionState.Showing, status!.State);
    }

    private static async Task StaleReadyIsIgnored()
    {
        using var context = await TestContext.CreateAsync();
        var response = await context.Coordinator.StartAsync(new StartGalleryRequest("module-1"), default);
        var first = await context.NextGalleryAsync();
        await context.Coordinator.ControlAsync(new ControlGalleryRequest(response.SessionId, GalleryControlAction.Next), default);
        var second = await context.NextGalleryAsync();
        await context.Coordinator.ReportAsync(new GalleryDisplayStatusReport("led-main", first.CommandId,
            response.SessionId, first.ImageId, GalleryDisplayState.Ready, null, DateTimeOffset.UtcNow), default);
        var staleResult = await context.Broker.WaitGalleryAsync("led-main", TimeSpan.FromMilliseconds(40), default);
        True(staleResult is null, "A stale Ready report emitted a Show command.");
        await context.Coordinator.ReportAsync(new GalleryDisplayStatusReport("led-main", second.CommandId,
            response.SessionId, second.ImageId, GalleryDisplayState.Ready, null, DateTimeOffset.UtcNow), default);
        Equal(GalleryDisplayAction.Show, (await context.NextGalleryAsync()).Action);
    }

    private static async Task ControlsAreDeterministic()
    {
        using var context = await TestContext.CreateAsync();
        var response = await context.Coordinator.StartAsync(new StartGalleryRequest("module-1"), default);
        await context.NextGalleryAsync();
        var previous = await context.Coordinator.ControlAsync(
            new ControlGalleryRequest(response.SessionId, GalleryControlAction.Previous), default);
        Equal(context.Images.Length, previous.Session!.CurrentImageNumber);
        await context.NextGalleryAsync();
        var shown = await context.Coordinator.ControlAsync(
            new ControlGalleryRequest(response.SessionId, GalleryControlAction.Show, 1), default);
        Equal(2, shown.Session!.CurrentImageNumber);
        await context.NextGalleryAsync();
        var slideshow = await context.Coordinator.ControlAsync(
            new ControlGalleryRequest(response.SessionId, GalleryControlAction.StartSlideshow,
                SlideshowIntervalSeconds: 30), default);
        True(slideshow.Session!.SlideshowActive, "Slideshow state was not persisted.");
        Equal(30, slideshow.Session.SlideshowIntervalSeconds);
        var paused = await context.Coordinator.ControlAsync(
            new ControlGalleryRequest(response.SessionId, GalleryControlAction.PauseSlideshow), default);
        True(!paused.Session!.SlideshowActive, "Pause did not disable slideshow state.");
    }

    private static async Task DisplayFailureIsPersisted()
    {
        using var context = await TestContext.CreateAsync();
        var response = await context.Coordinator.StartAsync(new StartGalleryRequest("module-1"), default);
        var prepare = await context.NextGalleryAsync();
        await context.Coordinator.ControlAsync(new ControlGalleryRequest(response.SessionId,
            GalleryControlAction.StartSlideshow, SlideshowIntervalSeconds: 30), default);
        await context.Coordinator.ReportAsync(new GalleryDisplayStatusReport("led-main", prepare.CommandId,
            response.SessionId, prepare.ImageId, GalleryDisplayState.Failed, "decode failed", DateTimeOffset.UtcNow), default);
        var status = await context.Coordinator.GetActiveAsync(default);
        Equal(GallerySessionState.Failed, status!.State);
        Equal("decode failed", status.Error);
        True(!status.SlideshowActive, "A failed display must pause slideshow advancement.");
    }

    private static async Task HigherPriorityActivityBlocksStart()
    {
        using var playbackContext = await TestContext.CreateAsync(playbackActive: true);
        await ThrowsAsync<InvalidOperationException>(() => playbackContext.Coordinator.StartAsync(
            new StartGalleryRequest("module-1"), default));
        using var welcomeContext = await TestContext.CreateAsync(welcomeActive: true);
        await ThrowsAsync<InvalidOperationException>(() => welcomeContext.Coordinator.StartAsync(
            new StartGalleryRequest("module-1"), default));
    }

    private static async Task HigherPriorityTransitionStopsGallery()
    {
        using var context = await TestContext.CreateAsync();
        await context.Coordinator.StartAsync(new StartGalleryRequest("module-1"), default);
        await context.NextGalleryAsync();
        var invoked = false;
        var result = await context.Coordinator.RunHigherPriorityAsync("formal playback", _ =>
        {
            invoked = true;
            return Task.FromResult(42);
        }, default);
        Equal(42, result);
        True(invoked, "Higher priority callback was not invoked.");
        Equal(GalleryDisplayAction.Stop, (await context.NextGalleryAsync()).Action);
        True(await context.Coordinator.GetActiveAsync(default) is null, "Gallery session remained active after preemption.");
    }

    private static async Task RestartAndReconnectRecover()
    {
        using var context = await TestContext.CreateAsync();
        var response = await context.Coordinator.StartAsync(new StartGalleryRequest("module-1", 2), default);
        await context.NextGalleryAsync();

        var broker = new CommandBroker();
        broker.Register(new ClientRegistration("led-main", ClientKind.LedPlayer, "test", context.Content.Version,
            true, "ready", "restarted-led"));
        var restored = context.CreateCoordinator(broker);
        var status = await restored.GetActiveAsync(default);
        Equal(response.SessionId, status!.SessionId);
        Equal(3, status.CurrentImageNumber);
        await restored.RecoverClientAsync("led-main", default);
        var command = await broker.WaitGalleryAsync("led-main", TimeSpan.FromSeconds(1), default);
        Equal(GalleryDisplayAction.Prepare, command!.Action);
        Equal(context.Images[2].Id, command.ImageId);
    }

    private static async Task VersionChangeDiscardsRecovery()
    {
        using var context = await TestContext.CreateAsync();
        await context.Coordinator.StartAsync(new StartGalleryRequest("module-1"), default);
        await context.NextGalleryAsync();
        await context.Repository.SaveAsync(context.Content.Modules, "new-version", default);
        var restored = context.CreateCoordinator(new CommandBroker());
        True(await restored.GetActiveAsync(default) is null, "Old gallery session survived a published content version change.");
    }

    private static async Task InvalidAssetIsRejected()
    {
        using var context = await TestContext.CreateAsync(invalidAsset: true);
        await ThrowsAsync<InvalidOperationException>(() => context.Coordinator.StartAsync(
            new StartGalleryRequest("module-1"), default));
    }

    private static async Task LedGateIsEnforced()
    {
        using var offline = await TestContext.CreateAsync(registerLed: false);
        await ThrowsAsync<InvalidOperationException>(() => offline.Coordinator.StartAsync(
            new StartGalleryRequest("module-1"), default));

        using var mismatch = await TestContext.CreateAsync(allowDegraded: false, ledVersionOffset: -1);
        await ThrowsAsync<InvalidOperationException>(() => mismatch.Coordinator.StartAsync(
            new StartGalleryRequest("module-1"), default));
    }

    private static async Task CommandChannelsAreIsolated()
    {
        var broker = new CommandBroker();
        var now = DateTimeOffset.UtcNow;
        await broker.PublishGalleryAsync("led-main", new GalleryDisplayCommand(1, "g", "s", "m", "i",
            GalleryDisplayAction.Prepare, 1, "i", "/i.png", new string('a', 64), 10, "image/png", now), default);
        var playback = await broker.WaitAsync("led-main", TimeSpan.FromMilliseconds(40), default);
        var welcome = await broker.WaitWelcomeAsync("led-main", TimeSpan.FromMilliseconds(40), default);
        True(playback is null && welcome is null, "Gallery command leaked into another command channel.");
        Equal("g", (await broker.WaitGalleryAsync("led-main", TimeSpan.FromSeconds(1), default))!.CommandId);
    }

    private static Task ContractsRoundTrip()
    {
        Equal(0, (int)GalleryDisplayAction.Prepare);
        Equal(1, (int)GalleryDisplayAction.Show);
        Equal(2, (int)GalleryDisplayAction.Stop);
        var source = new GalleryDisplayCommand(9, "command", "session", "module", "image",
            GalleryDisplayAction.Show, 7, "asset", "/media/image.png", new string('b', 64), 123,
            "image/png", DateTimeOffset.UtcNow, GalleryFitMode.Contain);
        var restored = JsonSerializer.Deserialize<GalleryDisplayCommand>(JsonSerializer.Serialize(source))!;
        Equal(source, restored);
        return Task.CompletedTask;
    }

    private static async Task ThrowsAsync<T>(Func<Task> action) where T : Exception
    {
        try
        {
            await action();
        }
        catch (T)
        {
            return;
        }
        throw new InvalidOperationException($"Expected {typeof(T).Name} was not thrown.");
    }

    private static void True(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected '{expected}', actual '{actual}'.");
    }

    private sealed class TestContext : IDisposable
    {
        private readonly TestHostEnvironment environment;
        private readonly IOptions<StorageOptions> storage;
        private readonly IOptions<PlaybackOptions> playbackOptions;
        private readonly ActivityState playbackState;
        private readonly ActivityState welcomeState;

        private TestContext(string directory, TestHostEnvironment environment, IOptions<StorageOptions> storage,
            IOptions<PlaybackOptions> playbackOptions, JsonContentRepository repository, CommandBroker broker,
            ActivityState playbackState, ActivityState welcomeState, ContentAsset[] images,
            PublishedContent content)
        {
            Directory = directory;
            this.environment = environment;
            this.storage = storage;
            this.playbackOptions = playbackOptions;
            Repository = repository;
            Broker = broker;
            this.playbackState = playbackState;
            this.welcomeState = welcomeState;
            Images = images;
            Content = content;
            Coordinator = CreateCoordinator(broker);
        }

        public string Directory { get; }
        public JsonContentRepository Repository { get; }
        public CommandBroker Broker { get; }
        public ImageGalleryCoordinator Coordinator { get; }
        public ContentAsset[] Images { get; }
        public PublishedContent Content { get; }

        public static async Task<TestContext> CreateAsync(bool playbackActive = false, bool welcomeActive = false,
            bool invalidAsset = false, bool registerLed = true, bool allowDegraded = true, long ledVersionOffset = 0)
        {
            var directory = Path.Combine(Path.GetTempPath(), "tg-gallery-b-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(directory);
            var environment = new TestHostEnvironment(directory);
            var storage = Options.Create(new StorageOptions { DataDirectory = "Data" });
            var options = Options.Create(new PlaybackOptions
            {
                LedClientId = "led-main",
                LongPollSeconds = 1,
                PrepareLeadMilliseconds = 250,
                RequireLedReadyBeforeStart = true,
                AllowDegradedPlayback = allowDegraded
            });
            var repository = new JsonContentRepository(storage, environment);
            var images = new[]
            {
                Image("image-1", "第一张", "image/png", '1'),
                Image("image-2", "第二张", "image/jpeg", '2'),
                Image("image-3", "第三张", "image/png", '3')
            };
            if (invalidAsset) images[1] = images[1] with { Sha256 = "bad", SizeBytes = 0 };
            var module = new ExhibitionModule("module-1", "一路向新", 1, "", null, true,
                Array.Empty<NarrationNode>(), images);
            var content = await repository.SaveAsync([module], "test", default);
            var broker = new CommandBroker();
            if (registerLed)
                broker.Register(new ClientRegistration("led-main", ClientKind.LedPlayer, "test",
                    content.Version + ledVersionOffset, true, "ready", "led-instance"));
            return new TestContext(directory, environment, storage, options, repository, broker,
                new ActivityState(playbackActive), new ActivityState(welcomeActive), images, content);
        }

        public ImageGalleryCoordinator CreateCoordinator(ICommandBroker broker) =>
            new(Repository, broker, playbackState, welcomeState, playbackOptions,
                new GallerySessionStore(storage, environment), new OperationalEventRepository(storage, environment),
                NullLogger<ImageGalleryCoordinator>.Instance);

        public async Task<GalleryDisplayCommand> NextGalleryAsync()
        {
            var command = await Broker.WaitGalleryAsync("led-main", TimeSpan.FromSeconds(1), default);
            return command ?? throw new InvalidOperationException("Expected gallery command was not published.");
        }

        public void Dispose()
        {
            try { System.IO.Directory.Delete(Directory, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private static ContentAsset Image(string id, string name, string mediaType, char hash) =>
            new(id, name, AssetKind.Image, $"/media/{id}.png", new string(hash, 64), 1024, 0, mediaType);
    }

    private sealed class ActivityState(bool active) : IPlaybackActivityState, IWelcomeActivityState
    {
        public bool IsProtectingFormalPlayback => active;
        public Task<bool> HasActiveSessionAsync(CancellationToken cancellationToken) => Task.FromResult(active);
    }

    private sealed class TestHostEnvironment(string root) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "TG.Control.GalleryB.Tests";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new PhysicalFileProvider(root);
    }
}
