using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Http.Features;
using TG.Control.Contracts;
using TG.Control.Server;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = null);
builder.Host.UseWindowsService(options => options.ServiceName = "TG Exhibition Control Server");
builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection(StorageOptions.SectionName));
builder.Services.Configure<PlaybackOptions>(builder.Configuration.GetSection(PlaybackOptions.SectionName));
builder.Services.Configure<TtsOptions>(builder.Configuration.GetSection(TtsOptions.SectionName));
builder.Services.Configure<AdminOptions>(builder.Configuration.GetSection(AdminOptions.SectionName));
builder.Services.AddSingleton<IContentRepository, JsonContentRepository>();
builder.Services.AddSingleton<ICommandBroker, CommandBroker>();
builder.Services.AddSingleton<PlaybackCoordinator>();
builder.Services.AddSingleton<ITtsService, UnconfiguredTtsService>();
builder.Services.AddSingleton<AdminSessionStore>();
builder.Services.AddSingleton<AssetStorage>();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy.AllowAnyHeader().AllowAnyMethod().AllowAnyOrigin()));
builder.Services.AddProblemDetails();

var app = builder.Build();
app.UseExceptionHandler();
app.UseCors();
app.UseDefaultFiles();
app.UseStaticFiles();
var assetStorage = app.Services.GetRequiredService<AssetStorage>();
app.UseStaticFiles(new StaticFileOptions { FileProvider = assetStorage.FileProvider, RequestPath = "/media" });

app.MapGet("/api/health", () => Results.Ok(new { status = "ok", utc = DateTimeOffset.UtcNow }));
app.MapPost("/api/auth/login", (LoginRequest request, AdminSessionStore sessions) =>
{
    var result = sessions.Login(request.Username, request.Password);
    return result is null ? Results.Json(new { message = "用户名或密码错误。" }, statusCode: StatusCodes.Status401Unauthorized) : Results.Ok(result);
});
app.MapGet("/api/auth/me", (HttpRequest request, AdminSessionStore sessions) =>
    sessions.TryValidate(request, out var username)
        ? Results.Ok(new { username })
        : Results.Unauthorized());
app.MapPost("/api/auth/logout", (HttpRequest request, AdminSessionStore sessions) =>
{
    sessions.Logout(request);
    return Results.NoContent();
});
app.MapGet("/api/content/current", (IContentRepository repository, CancellationToken ct) => repository.GetAsync(ct));
app.MapPost("/api/content/publish", async (HttpRequest httpRequest, PublishContentRequest request, AdminSessionStore sessions, IContentRepository repository, CancellationToken ct) =>
{
    if (!sessions.TryValidate(httpRequest, out var username)) return Results.Unauthorized();
    var validation = ContentValidator.Validate(request.Modules);
    if (validation.Count > 0) return Results.ValidationProblem(validation);
    return Results.Ok(await repository.SaveAsync(request.Modules, username, ct));
});
app.MapPost("/api/assets/upload", async (HttpContext context, AdminSessionStore sessions, AssetStorage storage, CancellationToken ct) =>
{
    if (!sessions.TryValidate(context.Request, out _)) return Results.Unauthorized();
    if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } sizeFeature)
        sizeFeature.MaxRequestBodySize = null;
    try
    {
        var asset = await storage.SaveAsync(context.Request, ct);
        var absoluteUrl = $"{context.Request.Scheme}://{context.Request.Host}{asset.Url}";
        return Results.Ok(asset with { Url = absoluteUrl });
    }
    catch (InvalidDataException exception)
    {
        return Results.BadRequest(new { message = exception.Message });
    }
});
app.MapDelete("/api/assets/{storedName}", (string storedName, HttpRequest request, AdminSessionStore sessions, AssetStorage storage) =>
{
    if (!sessions.TryValidate(request, out _)) return Results.Unauthorized();
    return storage.Delete(storedName) ? Results.NoContent() : Results.NotFound();
});
app.MapGet("/api/tts/status", (HttpRequest request, AdminSessionStore sessions, IOptions<TtsOptions> options) =>
    sessions.TryValidate(request, out _)
        ? Results.Ok(new { provider = options.Value.Provider, voice = options.Value.Voice, configured = !string.Equals(options.Value.Provider, "NotConfigured", StringComparison.OrdinalIgnoreCase) })
        : Results.Unauthorized());
app.MapPost("/api/clients/register", (ClientRegistration registration, ICommandBroker broker) =>
{
    broker.Register(registration);
    return Results.Ok(new { registered = true });
});
app.MapGet("/api/commands/next", async (string clientId, ICommandBroker broker, IOptions<PlaybackOptions> options, CancellationToken ct) =>
{
    var command = await broker.WaitAsync(clientId, TimeSpan.FromSeconds(options.Value.LongPollSeconds), ct);
    return command is null ? Results.NoContent() : Results.Ok(command);
});
app.MapGet("/api/clients/status", (HttpRequest request, AdminSessionStore sessions, ICommandBroker broker, IOptions<PlaybackOptions> options) =>
    sessions.TryValidate(request, out _)
        ? Results.Ok(broker.GetClientStatuses(TimeSpan.FromSeconds(Math.Max(10, options.Value.LongPollSeconds * 2 + 5))))
        : Results.Unauthorized());
app.MapPost("/api/playback/start", async (StartNarrationRequest request, PlaybackCoordinator coordinator, CancellationToken ct) =>
    Results.Ok(await coordinator.StartAsync(request, ct)));
app.MapPost("/api/playback/control", async (ControlNarrationRequest request, PlaybackCoordinator coordinator, CancellationToken ct) =>
{
    var result = await coordinator.ControlAsync(request, ct);
    return result.Accepted ? Results.Ok(result) : Results.NotFound(result);
});
app.MapGet("/api/playback/sessions", async (HttpRequest request, AdminSessionStore sessions, PlaybackCoordinator coordinator, CancellationToken ct) =>
    sessions.TryValidate(request, out _)
        ? Results.Ok(await coordinator.GetSessionsAsync(ct))
        : Results.Unauthorized());
app.MapGet("/api/playback/sessions/{sessionId}", async (string sessionId, PlaybackCoordinator coordinator, CancellationToken ct) =>
{
    var session = await coordinator.GetSessionAsync(sessionId, ct);
    return Results.Ok(new { active = session is not null, session });
});
app.MapPost("/api/playback/status", async (PlaybackStatusReport report, PlaybackCoordinator coordinator, ILoggerFactory loggerFactory, CancellationToken ct) =>
{
    loggerFactory.CreateLogger("PlaybackStatus").LogInformation(
        "Client {ClientId} command {CommandId} state {State} position {Position} error {Error}",
        report.ClientId, report.CommandId, report.State, report.PositionSeconds, report.Error);
    await coordinator.ReportAsync(report, ct);
    return Results.Accepted();
});
app.MapPost("/api/tts/synthesize", async (HttpRequest httpRequest, TtsSynthesisRequest request, AdminSessionStore sessions, ITtsService tts, CancellationToken ct) =>
{
    if (!sessions.TryValidate(httpRequest, out _)) return Results.Unauthorized();
    return Results.Ok(await tts.SynthesizeAsync(request, ct));
});
app.MapFallbackToFile("index.html");

app.Run();

public sealed record PublishContentRequest(IReadOnlyList<ExhibitionModule> Modules);
