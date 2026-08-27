using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using TG.Control.Contracts;

namespace TG.Control.Server;

public sealed class AssetStorage
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mov", ".mkv", ".webm", ".jpg", ".jpeg", ".png", ".webp", ".mp3", ".wav", ".aac", ".m4a"
    };

    public AssetStorage(IOptions<StorageOptions> options, IHostEnvironment environment)
    {
        var dataDirectory = Path.GetFullPath(options.Value.DataDirectory, environment.ContentRootPath);
        MediaDirectory = Path.Combine(dataDirectory, "Media");
        Directory.CreateDirectory(MediaDirectory);
        FileProvider = new PhysicalFileProvider(MediaDirectory);
    }

    public string MediaDirectory { get; }
    public IFileProvider FileProvider { get; }

    public string? ValidatePublishedReference(string url, long expectedSize, HostString requestHost)
    {
        if (string.IsNullOrWhiteSpace(url)) return "素材地址为空。";
        if (!Uri.TryCreate(url, UriKind.RelativeOrAbsolute, out var uri)) return $"素材地址格式无效：{url}";

        string path;
        if (uri.IsAbsoluteUri)
        {
            if (uri.Scheme is not ("http" or "https")) return $"不支持的素材地址协议：{uri.Scheme}";
            var belongsToThisServer = uri.IsLoopback ||
                                      string.Equals(uri.Host, requestHost.Host, StringComparison.OrdinalIgnoreCase);
            if (!belongsToThisServer) return "外部素材地址不能直接发布，请先上传到本系统素材库。";
            path = uri.AbsolutePath;
        }
        else
        {
            path = url.Split('?', '#')[0];
            if (!path.StartsWith('/')) path = "/" + path;
        }

        const string mediaPrefix = "/media/";
        if (!path.StartsWith(mediaPrefix, StringComparison.OrdinalIgnoreCase))
            return $"本服务器素材必须使用 {mediaPrefix} 地址。";

        var storedName = Uri.UnescapeDataString(path[mediaPrefix.Length..]);
        if (string.IsNullOrWhiteSpace(storedName) ||
            !string.Equals(storedName, Path.GetFileName(storedName), StringComparison.Ordinal) ||
            storedName.Contains('/') || storedName.Contains('\\'))
            return "素材文件名无效。";

        var filePath = Path.Combine(MediaDirectory, storedName);
        if (!File.Exists(filePath)) return $"服务器文件不存在（HTTP 404）：{path}";
        var actualSize = new FileInfo(filePath).Length;
        if (actualSize <= 0) return $"服务器文件为空：{path}";
        if (expectedSize > 0 && actualSize != expectedSize)
            return $"服务器文件大小不一致：记录 {expectedSize} 字节，实际 {actualSize} 字节。";
        return null;
    }

    public bool Delete(string storedName)
    {
        if (!string.Equals(storedName, Path.GetFileName(storedName), StringComparison.Ordinal)) return false;
        var path = Path.Combine(MediaDirectory, storedName);
        if (!File.Exists(path)) return false;
        File.Delete(path);
        return true;
    }

    public async Task<ContentAsset> SaveAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        var encodedName = request.Headers["X-File-Name"].ToString();
        var originalName = Uri.UnescapeDataString(encodedName);
        if (string.IsNullOrWhiteSpace(originalName)) throw new InvalidDataException("缺少文件名。");
        var extension = Path.GetExtension(originalName);
        if (!AllowedExtensions.Contains(extension)) throw new InvalidDataException($"不支持的文件格式：{extension}");
        if (!Enum.TryParse<AssetKind>(request.Headers["X-Asset-Kind"], true, out var kind)) throw new InvalidDataException("素材类型无效。");
        _ = double.TryParse(request.Headers["X-Duration-Seconds"], NumberStyles.Float, CultureInfo.InvariantCulture, out var durationSeconds);

        var storedName = $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        var targetPath = Path.Combine(MediaDirectory, storedName);
        long size = 0;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = ArrayPool<byte>.Shared.Rent(1024 * 1024);
        try
        {
            await using var target = new FileStream(targetPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, buffer.Length, FileOptions.Asynchronous | FileOptions.SequentialScan);
            while (true)
            {
                var read = await request.Body.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                if (read == 0) break;
                size += read;
                hash.AppendData(buffer, 0, read);
                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
        }
        catch
        {
            if (File.Exists(targetPath)) File.Delete(targetPath);
            throw;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        if (size == 0)
        {
            File.Delete(targetPath);
            throw new InvalidDataException("上传文件为空。");
        }

        return new ContentAsset(Guid.NewGuid().ToString("N"), originalName, kind, $"/media/{storedName}",
            Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant(), size, Math.Max(0, durationSeconds));
    }
}
