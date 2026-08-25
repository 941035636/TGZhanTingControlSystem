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
