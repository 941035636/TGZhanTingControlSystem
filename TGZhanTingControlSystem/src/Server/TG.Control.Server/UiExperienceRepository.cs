using System.Text.Json;
using Microsoft.Extensions.Options;
using TG.Control.Contracts;

namespace TG.Control.Server;

public sealed class UiExperienceRepository
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly string filePath;
    private readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public UiExperienceRepository(IOptions<StorageOptions> options, IHostEnvironment environment)
    {
        var directory = Path.GetFullPath(options.Value.DataDirectory, environment.ContentRootPath);
        Directory.CreateDirectory(directory);
        filePath = Path.Combine(directory, "ui-experience.json");
    }

    public async Task<UiExperienceConfig> GetAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(filePath))
            {
                var initial = Defaults();
                await WriteAsync(initial, cancellationToken);
                return initial;
            }

            await using var stream = File.OpenRead(filePath);
            return await JsonSerializer.DeserializeAsync<UiExperienceConfig>(stream, jsonOptions, cancellationToken) ?? Defaults();
        }
        finally { gate.Release(); }
    }

    public async Task<UiExperienceConfig> SaveAsync(UiExperienceConfig request, string updatedBy, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var currentVersion = 0L;
            if (File.Exists(filePath))
            {
                await using var stream = File.OpenRead(filePath);
                currentVersion = (await JsonSerializer.DeserializeAsync<UiExperienceConfig>(stream, jsonOptions, cancellationToken))?.Version ?? 0;
            }

            var saved = request with
            {
                Version = currentVersion + 1,
                TouchTitle = Clean(request.TouchTitle, "展厅自动讲解系统"),
                TouchSubtitle = Clean(request.TouchSubtitle, "智慧展陈中控终端"),
                TouchBackgroundColor = NormalizeColor(request.TouchBackgroundColor, "#EEF3F0"),
                TouchAccentColor = NormalizeColor(request.TouchAccentColor, "#1C5B46"),
                LedTitle = Clean(request.LedTitle, "展厅自动讲解系统"),
                LedSubtitle = Clean(request.LedSubtitle, "等待触控终端启动讲解"),
                LedIdleMediaKind = request.LedIdleMediaKind?.ToLowerInvariant() is "image" or "video" ? request.LedIdleMediaKind.ToLowerInvariant() : "none",
                LedBackgroundColor = NormalizeColor(request.LedBackgroundColor, "#0A1F1B"),
                UpdatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedBy = updatedBy
            };
            await WriteAsync(saved, cancellationToken);
            return saved;
        }
        finally { gate.Release(); }
    }

    private async Task WriteAsync(UiExperienceConfig value, CancellationToken cancellationToken)
    {
        var tempPath = filePath + ".tmp";
        await using (var stream = File.Create(tempPath))
            await JsonSerializer.SerializeAsync(stream, value, jsonOptions, cancellationToken);
        File.Move(tempPath, filePath, true);
    }

    private static UiExperienceConfig Defaults() => new(
        0, "展厅自动讲解系统", "TG EXHIBITION · 智慧展陈中控终端", null, "#EEF3F0", "#1C5B46",
        "展厅自动讲解系统", "等待触控终端启动讲解", null, "none", "#0A1F1B", true, true,
        DateTimeOffset.MinValue, "system");

    private static string Clean(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    private static string NormalizeColor(string? value, string fallback) =>
        !string.IsNullOrWhiteSpace(value) && System.Text.RegularExpressions.Regex.IsMatch(value, "^#[0-9a-fA-F]{6}$") ? value : fallback;
}
