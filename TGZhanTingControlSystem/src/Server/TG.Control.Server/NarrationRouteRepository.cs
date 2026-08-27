using System.Text.Json;
using Microsoft.Extensions.Options;
using TG.Control.Contracts;

namespace TG.Control.Server;

public sealed class NarrationRouteRepository
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly string filePath;
    private readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public NarrationRouteRepository(IOptions<StorageOptions> options, IHostEnvironment environment)
    {
        var directory = Path.GetFullPath(options.Value.DataDirectory, environment.ContentRootPath);
        Directory.CreateDirectory(directory);
        filePath = Path.Combine(directory, "narration-routes.json");
    }

    public async Task<IReadOnlyList<NarrationRoute>> GetAllAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try { return await ReadAsync(cancellationToken); }
        finally { gate.Release(); }
    }

    public async Task<NarrationRoute> SaveAsync(SaveNarrationRouteRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name)) throw new ArgumentException("路线名称不能为空。", nameof(request));
        if (request.ModuleIds.Count == 0) throw new ArgumentException("路线至少需要一个主题。", nameof(request));

        await gate.WaitAsync(cancellationToken);
        try
        {
            var routes = (await ReadAsync(cancellationToken)).ToList();
            var id = string.IsNullOrWhiteSpace(request.Id) ? Guid.NewGuid().ToString("N") : request.Id;
            var route = new NarrationRoute(id, request.Name.Trim(), request.ModuleIds.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), DateTimeOffset.UtcNow);
            var existingIndex = routes.FindIndex(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
            if (existingIndex >= 0) routes[existingIndex] = route; else routes.Add(route);
            await WriteAsync(routes, cancellationToken);
            return route;
        }
        finally { gate.Release(); }
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var routes = (await ReadAsync(cancellationToken)).ToList();
            var removed = routes.RemoveAll(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase)) > 0;
            if (removed) await WriteAsync(routes, cancellationToken);
            return removed;
        }
        finally { gate.Release(); }
    }

    private async Task<IReadOnlyList<NarrationRoute>> ReadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath)) return [];
        await using var stream = File.OpenRead(filePath);
        return await JsonSerializer.DeserializeAsync<List<NarrationRoute>>(stream, jsonOptions, cancellationToken) ?? [];
    }

    private async Task WriteAsync(IReadOnlyList<NarrationRoute> routes, CancellationToken cancellationToken)
    {
        var tempPath = filePath + ".tmp";
        await using (var stream = File.Create(tempPath))
            await JsonSerializer.SerializeAsync(stream, routes, jsonOptions, cancellationToken);
        File.Move(tempPath, filePath, true);
    }
}
