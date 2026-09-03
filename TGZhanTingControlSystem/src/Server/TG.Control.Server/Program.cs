using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Http.Features;
using TG.Control.Contracts;
using TG.Control.Server;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    WebRootPath = Path.Combine(AppContext.BaseDirectory, "AdminWeb")
});
AddSiteConfiguration(builder.Configuration, args);
var fileLogDirectory = builder.Configuration["Logging:FileDirectory"];
if (!string.IsNullOrWhiteSpace(fileLogDirectory))
    builder.Logging.AddProvider(new DailyFileLoggerProvider(fileLogDirectory));
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = null);
builder.Host.UseWindowsService(options => options.ServiceName = "TG Exhibition Control Server");
builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection(StorageOptions.SectionName));
builder.Services.Configure<PlaybackOptions>(builder.Configuration.GetSection(PlaybackOptions.SectionName));
builder.Services.Configure<TtsOptions>(builder.Configuration.GetSection(TtsOptions.SectionName));
builder.Services.Configure<TtsProductionOptions>(builder.Configuration.GetSection(TtsProductionOptions.SectionName));
builder.Services.Configure<MeloTtsLocalOptions>(builder.Configuration.GetSection(MeloTtsLocalOptions.SectionName));
builder.Services.Configure<AdminOptions>(builder.Configuration.GetSection(AdminOptions.SectionName));
builder.Services.Configure<TerminalOptions>(builder.Configuration.GetSection(TerminalOptions.SectionName));
builder.Services.AddSingleton<IContentRepository, JsonContentRepository>();
builder.Services.AddSingleton<ICommandBroker, CommandBroker>();
builder.Services.AddSingleton<PlaybackCoordinator>();
builder.Services.AddSingleton<ITtsService, UnconfiguredTtsService>();
if (builder.Environment.IsDevelopment() &&
    builder.Configuration.GetValue<bool>($"{TtsProductionOptions.SectionName}:EnableDeterministicTestProvider"))
    builder.Services.AddSingleton<ITtsProvider, DeterministicTestTtsProvider>();
if (builder.Configuration.GetValue<bool>($"{MeloTtsLocalOptions.SectionName}:Enabled", true))
{
    builder.Services.AddHttpClient(MeloTtsLocalProvider.HttpClientName)
        .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { UseProxy = false });
    builder.Services.AddSingleton<MeloTtsWorkerSupervisor>();
    builder.Services.AddHostedService(services => services.GetRequiredService<MeloTtsWorkerSupervisor>());
    builder.Services.AddSingleton<MeloTtsLocalProvider>();
    builder.Services.AddSingleton<ITtsProvider>(services => services.GetRequiredService<MeloTtsLocalProvider>());
}
builder.Services.AddSingleton<TtsProviderRegistry>();
builder.Services.AddSingleton<TtsProductionRepository>();
builder.Services.AddSingleton<TtsMediaValidator>();
builder.Services.AddSingleton<TtsProductionService>();
builder.Services.AddHostedService(services => services.GetRequiredService<TtsProductionService>());
builder.Services.AddSingleton<AdminSessionStore>();
builder.Services.AddSingleton<AssetStorage>();
builder.Services.AddSingleton<AssetReferenceProtectionService>();
builder.Services.AddSingleton<NarrationAudioBindingService>();
builder.Services.AddSingleton<ContentDraftRepository>();
builder.Services.AddSingleton<ContentDraftWorkflowService>();
builder.Services.AddSingleton<NarrationRouteRepository>();
builder.Services.AddSingleton<UiExperienceRepository>();
builder.Services.AddSingleton<PlaybackSessionStore>();
builder.Services.AddSingleton<OperationalEventRepository>();
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
app.MapGet("/api/content/draft", async (HttpRequest request, AdminSessionStore sessions,
    ContentDraftWorkflowService drafts, CancellationToken ct) =>
{
    if (!sessions.TryValidate(request, out _)) return Results.Unauthorized();
    return Results.Ok(await drafts.GetAsync(request.Host, ct));
});
app.MapPut("/api/content/draft", async (HttpRequest httpRequest, SaveContentDraftRequest request,
    AdminSessionStore sessions, ContentDraftWorkflowService drafts, CancellationToken ct) =>
{
    if (!sessions.TryValidate(httpRequest, out var username)) return Results.Unauthorized();
    try { return Results.Ok(await drafts.SaveAsync(request, username, httpRequest.Host, ct)); }
    catch (ContentDraftWorkflowException exception) { return DraftWorkflowError(exception); }
});
app.MapGet("/api/ui/current", (UiExperienceRepository repository, CancellationToken ct) => repository.GetAsync(ct));
app.MapPost("/api/ui/publish", async (HttpRequest request, UiExperienceConfig config, AdminSessionStore sessions,
    UiExperienceRepository repository, AssetStorage assetStorage, CancellationToken ct) =>
{
    if (!sessions.TryValidate(request, out var username)) return Results.Unauthorized();
    var validation = new Dictionary<string, string[]>();
    var touchError = string.IsNullOrWhiteSpace(config.TouchBackgroundUrl)
        ? null
        : assetStorage.ValidatePublishedReference(config.TouchBackgroundUrl, 0, request.Host);
    if (touchError is not null) validation["touchBackgroundUrl"] = [$"触控中控端背景素材：{touchError}"];
    var ledError = string.IsNullOrWhiteSpace(config.LedIdleMediaUrl)
        ? null
        : assetStorage.ValidatePublishedReference(config.LedIdleMediaUrl, 0, request.Host);
    if (ledError is not null) validation["ledIdleMediaUrl"] = [$"LED待机素材：{ledError}"];
    if (validation.Count > 0) return Results.ValidationProblem(validation);
    return Results.Ok(await repository.SaveAsync(config, username, ct));
});
app.MapGet("/api/content/manifest", async (IContentRepository repository, CancellationToken ct) =>
{
    var content = await repository.GetAsync(ct);
    return Results.Ok(ContentManifestBuilder.Build(content));
});
app.MapGet("/api/routes", async (NarrationRouteRepository repository, CancellationToken ct) =>
    Results.Ok(new { routes = await repository.GetAllAsync(ct) }));
app.MapPost("/api/routes", async (HttpRequest httpRequest, SaveNarrationRouteRequest request, NarrationRouteRepository repository,
    AdminSessionStore sessions, IOptions<TerminalOptions> terminal, OperationalEventRepository events, CancellationToken ct) =>
{
    if (!HasOperatorAccess(httpRequest, sessions, terminal, out var actor)) return Results.Unauthorized();
    try
    {
        var route = await repository.SaveAsync(request, ct);
        await events.AppendAsync("Information", "Route", "Save", $"{actor} 保存了讲解路线“{route.Name}”。", detail: route.Id, cancellationToken: ct);
        return Results.Ok(route);
    }
    catch (ArgumentException exception) { return Results.BadRequest(new { message = exception.Message }); }
});
app.MapDelete("/api/routes/{id}", async (string id, HttpRequest httpRequest, NarrationRouteRepository repository,
    AdminSessionStore sessions, IOptions<TerminalOptions> terminal, OperationalEventRepository events, CancellationToken ct) =>
{
    if (!HasOperatorAccess(httpRequest, sessions, terminal, out var actor)) return Results.Unauthorized();
    if (!await repository.DeleteAsync(id, ct)) return Results.NotFound();
    await events.AppendAsync("Warning", "Route", "Delete", $"{actor} 删除了讲解路线。", detail: id, cancellationToken: ct);
    return Results.NoContent();
});
app.MapPost("/api/content/publish", async (HttpRequest httpRequest, PublishContentRequest request, AdminSessionStore sessions,
    ContentDraftWorkflowService drafts,
    OperationalEventRepository events, CancellationToken ct) =>
{
    if (!sessions.TryValidate(httpRequest, out var username)) return Results.Unauthorized();
    var modules = NarrationAudioCompatibility.NormalizeModules(request.Modules);
    if (!request.BaseContentVersion.HasValue || !request.ExpectedDraftRevision.HasValue)
        return Results.BadRequest(new
        {
            code = "publish_revision_required",
            message = "发布必须携带当前正式版本和草稿修订号，请刷新后重试。"
        });
    try
    {
        var result = await drafts.PublishAsync(new SaveContentDraftRequest(request.BaseContentVersion.Value,
            request.ExpectedDraftRevision.Value, modules), username, httpRequest.Host, ct);
        await events.AppendAsync("Information", "Content", "Publish",
            $"{username} published content V{result.Version}.", cancellationToken: ct);
        return Results.Ok(result);
    }
    catch (ContentDraftValidationException exception) { return Results.ValidationProblem(exception.Errors); }
    catch (ContentDraftWorkflowException exception) { return DraftWorkflowError(exception); }
});
app.MapGet("/api/content/versions", async (HttpRequest request, AdminSessionStore sessions, IContentRepository repository, CancellationToken ct) =>
    sessions.TryValidate(request, out _) ? Results.Ok(await repository.GetHistoryAsync(ct)) : Results.Unauthorized());
app.MapPost("/api/content/rollback/{version:long}", async (long version, HttpRequest request,
    RollbackContentRequest rollbackRequest, AdminSessionStore sessions, ContentDraftWorkflowService drafts,
    OperationalEventRepository events, CancellationToken ct) =>
{
    if (!sessions.TryValidate(request, out var username)) return Results.Unauthorized();
    try
    {
        var content = await drafts.RollbackAsync(version, rollbackRequest, username, request.Host, ct);
        await events.AppendAsync("Warning", "Content", "Rollback", $"{username} 将内容回滚至 V{version}，生成新版本 V{content.Version}。", cancellationToken: ct);
        return Results.Ok(content);
    }
    catch (ContentDraftValidationException exception) { return Results.ValidationProblem(exception.Errors); }
    catch (ContentDraftWorkflowException exception) { return DraftWorkflowError(exception); }
    catch (KeyNotFoundException exception) { return Results.NotFound(new { message = exception.Message }); }
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
app.MapDelete("/api/assets/{storedName}", async (string storedName, HttpRequest request,
    AdminSessionStore sessions, AssetReferenceProtectionService protection, CancellationToken ct) =>
{
    if (!sessions.TryValidate(request, out _)) return Results.Unauthorized();
    var result = await protection.DeleteIfUnreferencedAsync(storedName, ct);
    if (result.Protected)
        return Results.Conflict(new
        {
            code = "asset_is_referenced",
            message = "该素材仍被草稿、正式版本、历史版本或有效候选语音引用，不能删除。",
            references = result.References
        });
    return result.Deleted ? Results.NoContent() : Results.NotFound();
});
app.MapPost("/api/narration-audio/bind-upload", (HttpRequest httpRequest,
    CreateManualNarrationAudioBindingRequest request, AdminSessionStore sessions,
    NarrationAudioBindingService bindingService) =>
{
    if (!sessions.TryValidate(httpRequest, out _)) return Results.Unauthorized();
    try { return Results.Ok(bindingService.CreateManualBinding(request, httpRequest.Host)); }
    catch (InvalidDataException exception) { return Results.BadRequest(new { message = exception.Message }); }
});
app.MapGet("/api/tts/status", (HttpRequest request, AdminSessionStore sessions, IOptions<TtsOptions> options) =>
    sessions.TryValidate(request, out _)
        ? Results.Ok(new { provider = options.Value.Provider, voice = options.Value.Voice, configured = !string.Equals(options.Value.Provider, "NotConfigured", StringComparison.OrdinalIgnoreCase) })
        : Results.Unauthorized());
app.MapGet("/api/tts/providers", async (HttpRequest request, AdminSessionStore sessions,
    TtsProductionService production, CancellationToken ct) =>
    sessions.TryValidate(request, out _)
        ? Results.Ok(await production.GetProvidersAsync(ct))
        : Results.Unauthorized());
app.MapPost("/api/tts/jobs", async (HttpRequest httpRequest, CreateTtsProductionJobRequest request,
    AdminSessionStore sessions, TtsProductionService production, CancellationToken ct) =>
{
    if (!sessions.TryValidate(httpRequest, out var username)) return Results.Unauthorized();
    try
    {
        var result = await production.CreateAsync(request, username, ct);
        return result.Created ? Results.Accepted($"/api/tts/jobs/{result.Job.JobId}", result) : Results.Ok(result);
    }
    catch (TtsProductionRequestException exception)
    {
        return Results.BadRequest(new { code = exception.ErrorCode, message = exception.Message });
    }
});
app.MapGet("/api/tts/jobs/{jobId}", async (string jobId, HttpRequest request, AdminSessionStore sessions,
    TtsProductionService production, CancellationToken ct) =>
{
    if (!sessions.TryValidate(request, out _)) return Results.Unauthorized();
    var job = await production.GetJobAsync(jobId, ct);
    return job is null ? Results.NotFound() : Results.Ok(job);
});
app.MapPost("/api/tts/jobs/{jobId}/cancel", async (string jobId, HttpRequest request,
    AdminSessionStore sessions, TtsProductionService production, CancellationToken ct) =>
{
    if (!sessions.TryValidate(request, out _)) return Results.Unauthorized();
    var job = await production.CancelAsync(jobId, ct);
    return job is null ? Results.NotFound() : Results.Ok(job);
});
app.MapGet("/api/tts/candidates/{candidateId}", async (string candidateId, HttpRequest request,
    AdminSessionStore sessions, TtsProductionService production, CancellationToken ct) =>
{
    if (!sessions.TryValidate(request, out _)) return Results.Unauthorized();
    var candidate = await production.GetCandidateAsync(candidateId, ct);
    return candidate is null ? Results.NotFound() : Results.Ok(candidate);
});
app.MapGet("/api/tts/candidates/{candidateId}/evaluation", async (string candidateId, HttpRequest request,
    AdminSessionStore sessions, ContentDraftWorkflowService drafts, CancellationToken ct) =>
{
    if (!sessions.TryValidate(request, out _)) return Results.Unauthorized();
    return Results.Ok(await drafts.EvaluateCandidateAsync(candidateId, request.Host, ct));
});
app.MapPost("/api/tts/candidates/{candidateId}/adopt", async (string candidateId, HttpRequest httpRequest,
    AdoptNarrationAudioCandidateRequest request, AdminSessionStore sessions,
    ContentDraftWorkflowService drafts, CancellationToken ct) =>
{
    if (!sessions.TryValidate(httpRequest, out var username)) return Results.Unauthorized();
    try { return Results.Ok(await drafts.AdoptAsync(candidateId, request, username, httpRequest.Host, ct)); }
    catch (ContentDraftWorkflowException exception) { return DraftWorkflowError(exception); }
});
app.MapPost("/api/clients/register", async (HttpRequest request, ClientRegistration registration, ICommandBroker broker,
    PlaybackCoordinator coordinator, IOptions<TerminalOptions> terminal, CancellationToken ct) =>
{
    if (!HasTerminalAccess(request, terminal)) return Results.Unauthorized();
    var newInstance = broker.Register(registration);
    if (newInstance) await coordinator.RecoverClientAsync(registration.ClientId, ct);
    return Results.Ok(new { registered = true });
});
app.MapGet("/api/commands/next", async (string clientId, HttpRequest request, ICommandBroker broker,
    IOptions<PlaybackOptions> options, IOptions<TerminalOptions> terminal, CancellationToken ct) =>
{
    if (!HasTerminalAccess(request, terminal)) return Results.Unauthorized();
    var command = await broker.WaitAsync(clientId, TimeSpan.FromSeconds(options.Value.LongPollSeconds), ct);
    return command is null ? Results.NoContent() : Results.Ok(command);
});
app.MapGet("/api/clients/status", (HttpRequest request, AdminSessionStore sessions, ICommandBroker broker, IOptions<PlaybackOptions> options) =>
    sessions.TryValidate(request, out _)
        ? Results.Ok(broker.GetClientStatuses(TimeSpan.FromSeconds(Math.Max(10, options.Value.LongPollSeconds * 2 + 5))))
        : Results.Unauthorized());
app.MapGet("/api/readiness", async (HttpRequest request, AdminSessionStore sessions, IOptions<TerminalOptions> terminal,
    ICommandBroker broker, IOptions<PlaybackOptions> playback, IContentRepository repository, CancellationToken ct) =>
{
    if (!HasOperatorAccess(request, sessions, terminal, out _)) return Results.Unauthorized();
    var content = await repository.GetAsync(ct);
    var threshold = TimeSpan.FromSeconds(Math.Max(10, playback.Value.LongPollSeconds * 2 + 5));
    var led = broker.GetClientStatuses(threshold)
        .FirstOrDefault(item => string.Equals(item.ClientId, playback.Value.LedClientId, StringComparison.OrdinalIgnoreCase));
    var online = led?.Online == true;
    var versionMatches = online && led?.ContentVersion == content.Version;
    var fullyReady = versionMatches && led?.Ready == true;
    var canStart = online && (fullyReady || playback.Value.AllowDegradedPlayback);
    var message = !online ? "LED播放端离线"
        : fullyReady ? "服务器、LED和讲解内容均已就绪"
        : !versionMatches && playback.Value.AllowDegradedPlayback
            ? $"LED内容 V{led?.ContentVersion ?? 0} 与服务器 V{content.Version} 不一致；仍可开始，当前节点素材将按需下载"
        : !versionMatches ? $"LED正在同步内容 V{content.Version}（当前 V{led?.ContentVersion ?? 0}）"
        : playback.Value.AllowDegradedPlayback
            ? (string.IsNullOrWhiteSpace(led?.Status) ? "部分素材未就绪，系统受限可用；失败节点将按策略跳过" : led.Status + "；系统受限可用")
            : led?.Status ?? "LED正在准备素材";
    return Results.Ok(new SystemReadiness(canStart, content.Version, online, led?.Ready == true,
        led?.ContentVersion ?? 0, message, DateTimeOffset.UtcNow));
});
app.MapPost("/api/playback/start", async (HttpRequest httpRequest, StartNarrationRequest request,
    AdminSessionStore sessions, IOptions<TerminalOptions> terminal, PlaybackCoordinator coordinator, CancellationToken ct) =>
{
    if (!HasOperatorAccess(httpRequest, sessions, terminal, out _)) return Results.Unauthorized();
    try { return Results.Ok(await coordinator.StartAsync(request, ct)); }
    catch (InvalidOperationException exception) { return Results.Conflict(new { message = exception.Message }); }
    catch (KeyNotFoundException exception) { return Results.BadRequest(new { message = exception.Message }); }
});
app.MapPost("/api/playback/control", async (HttpRequest httpRequest, ControlNarrationRequest request,
    AdminSessionStore sessions, IOptions<TerminalOptions> terminal, PlaybackCoordinator coordinator, CancellationToken ct) =>
{
    if (!HasOperatorAccess(httpRequest, sessions, terminal, out _)) return Results.Unauthorized();
    var result = await coordinator.ControlAsync(request, ct);
    return result.Accepted ? Results.Ok(result) : Results.NotFound(result);
});
app.MapGet("/api/playback/sessions", async (HttpRequest request, AdminSessionStore sessions, PlaybackCoordinator coordinator, CancellationToken ct) =>
    sessions.TryValidate(request, out _)
        ? Results.Ok(await coordinator.GetSessionsAsync(ct))
        : Results.Unauthorized());
app.MapGet("/api/playback/active", async (HttpRequest request, AdminSessionStore sessions, IOptions<TerminalOptions> terminal,
    PlaybackCoordinator coordinator, CancellationToken ct) =>
{
    if (!HasOperatorAccess(request, sessions, terminal, out _)) return Results.Unauthorized();
    var active = (await coordinator.GetSessionsAsync(ct)).FirstOrDefault();
    return Results.Ok(new { active = active is not null, session = active });
});
app.MapGet("/api/playback/sessions/{sessionId}", async (string sessionId, HttpRequest request,
    AdminSessionStore sessions, IOptions<TerminalOptions> terminal, PlaybackCoordinator coordinator, CancellationToken ct) =>
{
    if (!HasOperatorAccess(request, sessions, terminal, out _)) return Results.Unauthorized();
    var session = await coordinator.GetSessionAsync(sessionId, ct);
    return Results.Ok(new { active = session is not null, session });
});
app.MapPost("/api/playback/status", async (HttpRequest request, PlaybackStatusReport report,
    IOptions<TerminalOptions> terminal, PlaybackCoordinator coordinator, ILoggerFactory loggerFactory, CancellationToken ct) =>
{
    if (!HasTerminalAccess(request, terminal)) return Results.Unauthorized();
    loggerFactory.CreateLogger("PlaybackStatus").LogInformation(
        "Client {ClientId} command {CommandId} state {State} position {Position} error {Error}",
        report.ClientId, report.CommandId, report.State, report.PositionSeconds, report.Error);
    await coordinator.ReportAsync(report, ct);
    return Results.Accepted();
});
app.MapGet("/api/operations", async (HttpRequest request, int? count, AdminSessionStore sessions,
    OperationalEventRepository events, CancellationToken ct) =>
    sessions.TryValidate(request, out _) ? Results.Ok(await events.GetRecentAsync(count ?? 200, ct)) : Results.Unauthorized());
app.MapPost("/api/tts/synthesize", async (HttpRequest httpRequest, TtsSynthesisRequest request, AdminSessionStore sessions, ITtsService tts, CancellationToken ct) =>
{
    if (!sessions.TryValidate(httpRequest, out _)) return Results.Unauthorized();
    return Results.Ok(await tts.SynthesizeAsync(request, ct));
});
app.MapFallbackToFile("index.html");

app.Run();

static bool HasTerminalAccess(HttpRequest request, IOptions<TerminalOptions> options) =>
    !string.IsNullOrWhiteSpace(options.Value.ApiKey) &&
    string.Equals(request.Headers["X-TG-Terminal-Key"].ToString(), options.Value.ApiKey, StringComparison.Ordinal);

static bool HasOperatorAccess(HttpRequest request, AdminSessionStore sessions, IOptions<TerminalOptions> terminal, out string actor)
{
    if (sessions.TryValidate(request, out actor)) return true;
    if (HasTerminalAccess(request, terminal)) { actor = "touch-terminal"; return true; }
    actor = string.Empty;
    return false;
}

static void AddSiteConfiguration(ConfigurationManager configuration, string[] commandLineArguments)
{
    var programDataConfig = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "TG Exhibition",
        "Config",
        "server.site.json");
    configuration.AddJsonFile(programDataConfig, optional: true, reloadOnChange: true);

    var explicitSiteConfig = Environment.GetEnvironmentVariable("TG_SERVER_SITE_CONFIG");
    if (!string.IsNullOrWhiteSpace(explicitSiteConfig))
        configuration.AddJsonFile(Path.GetFullPath(explicitSiteConfig), optional: false, reloadOnChange: true);

    // Site configuration is loaded after appsettings.json, while environment variables and command-line values
    // remain the final override layers for development, automated tests and service recovery tooling.
    configuration.AddEnvironmentVariables();
    if (commandLineArguments.Length > 0) configuration.AddCommandLine(commandLineArguments);
}

static IResult DraftWorkflowError(ContentDraftWorkflowException exception) => exception.Failure switch
{
    ContentDraftWorkflowFailure.NotFound => Results.NotFound(new { code = exception.ErrorCode, message = exception.Message }),
    ContentDraftWorkflowFailure.InvalidAsset => Results.BadRequest(new { code = exception.ErrorCode, message = exception.Message }),
    _ => Results.Conflict(new { code = exception.ErrorCode, message = exception.Message })
};

public sealed record PublishContentRequest(IReadOnlyList<ExhibitionModule> Modules,
    long? BaseContentVersion = null, long? ExpectedDraftRevision = null);
