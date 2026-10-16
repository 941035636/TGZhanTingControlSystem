using System;
using System.Collections;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace TG.Control.LedPlayer
{
    public sealed class LedContentCache
    {
        public static LedContentCache Shared { get; } = new LedContentCache();
        private readonly System.Collections.Generic.Dictionary<string, ValidationMetadata> validationByUrl =
            new System.Collections.Generic.Dictionary<string, ValidationMetadata>(StringComparer.OrdinalIgnoreCase);
        private readonly System.Collections.Generic.Dictionary<string, ValidatedFileMetadata> validatedFilesByUrl =
            new System.Collections.Generic.Dictionary<string, ValidatedFileMetadata>(StringComparer.OrdinalIgnoreCase);
        private string contentDirectory;

        public void ConfigureDirectory(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory)) return;
            var resolved = Path.GetFullPath(Environment.ExpandEnvironmentVariables(directory));
            Directory.CreateDirectory(resolved);
            contentDirectory = resolved;
        }

        public void RegisterValidation(string mediaUrl, long expectedSize, string expectedSha256)
        {
            if (string.IsNullOrWhiteSpace(mediaUrl)) return;
            var normalizedSha256 = NormalizeSha256(expectedSha256);
            if (validationByUrl.TryGetValue(mediaUrl, out var previous) &&
                (previous.SizeBytes != expectedSize ||
                 !string.Equals(previous.Sha256, normalizedSha256, StringComparison.OrdinalIgnoreCase)))
                validatedFilesByUrl.Remove(mediaUrl);
            validationByUrl[mediaUrl] = new ValidationMetadata(expectedSize, normalizedSha256);
        }

        public bool HasExpectedFile(string mediaUrl, long expectedSize)
        {
            if (string.IsNullOrWhiteSpace(mediaUrl)) return false;
            if (mediaUrl.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
                return Uri.TryCreate(mediaUrl, UriKind.Absolute, out var fileUri) && File.Exists(fileUri.LocalPath);
            if (Path.IsPathRooted(mediaUrl)) return File.Exists(mediaUrl);
            try
            {
                var file = new FileInfo(GetCachePath(mediaUrl));
                return file.Exists && file.Length > 0 && (expectedSize <= 0 || file.Length == expectedSize);
            }
            catch
            {
                return false;
            }
        }

        public IEnumerator Resolve(string mediaUrl, Action<string> success, Action<string> failure, long expectedSize = 0,
            Action<float> progress = null, string expectedSha256 = null)
        {
            if (string.IsNullOrWhiteSpace(mediaUrl))
            {
                failure("媒体URL为空。");
                yield break;
            }

            if (validationByUrl.TryGetValue(mediaUrl, out var metadata))
            {
                if (expectedSize <= 0) expectedSize = metadata.SizeBytes;
                if (string.IsNullOrWhiteSpace(expectedSha256)) expectedSha256 = metadata.Sha256;
            }

            if (mediaUrl.StartsWith("file://", StringComparison.OrdinalIgnoreCase) || Path.IsPathRooted(mediaUrl))
            {
                var localPath = mediaUrl.StartsWith("file://", StringComparison.OrdinalIgnoreCase)
                    ? new Uri(mediaUrl).LocalPath
                    : mediaUrl;
                bool localValid = false;
                string localError = null;
                yield return ValidateFile(localPath, expectedSize, expectedSha256,
                    (ok, error) => { localValid = ok; localError = error; });
                if (!localValid)
                {
                    failure(localError);
                    yield break;
                }
                success(new Uri(Path.GetFullPath(localPath)).AbsoluteUri);
                yield break;
            }

            var finalPath = GetCachePath(mediaUrl);
            if (File.Exists(finalPath))
            {
                if (CanReuseValidatedFile(mediaUrl, finalPath, expectedSize, expectedSha256))
                {
                    Debug.Log("LED已复用同步阶段校验通过的本地素材，无需重复计算SHA-256：" + Path.GetFileName(finalPath));
                    success(new Uri(finalPath).AbsoluteUri);
                    yield break;
                }

                bool valid = false;
                string validationError = null;
                yield return ValidateFile(finalPath, expectedSize, expectedSha256,
                    (ok, error) => { valid = ok; validationError = error; });
                if (valid)
                {
                    RememberValidatedFile(mediaUrl, finalPath, expectedSize, expectedSha256);
                    success(new Uri(finalPath).AbsoluteUri);
                    yield break;
                }
                Debug.LogWarning("LED缓存文件校验失败，将重新下载：" + validationError);
                validatedFilesByUrl.Remove(mediaUrl);
                File.Delete(finalPath);
            }

            var partialPath = finalPath + ".partial";
            var existingLength = File.Exists(partialPath) ? new FileInfo(partialPath).Length : 0;
            if (existingLength > 0 && expectedSize > 0 && existingLength >= expectedSize)
            {
                var partialValid = false;
                string partialError = null;
                yield return ValidateFile(partialPath, expectedSize, expectedSha256,
                    (ok, error) => { partialValid = ok; partialError = error; });
                if (partialValid)
                {
                    File.Move(partialPath, finalPath);
                    RememberValidatedFile(mediaUrl, finalPath, expectedSize, expectedSha256);
                    success(new Uri(finalPath).AbsoluteUri);
                    yield break;
                }

                Debug.LogWarning("LED临时缓存无法继续，将从头下载：" + partialError);
                File.Delete(partialPath);
                existingLength = 0;
            }

            using (var request = UnityWebRequest.Get(mediaUrl))
            {
                request.downloadHandler = new DownloadHandlerFile(partialPath, existingLength > 0);
                if (existingLength > 0) request.SetRequestHeader("Range", "bytes=" + existingLength + "-");
                var operation = request.SendWebRequest();
                while (!operation.isDone)
                {
                    progress?.Invoke(request.downloadProgress);
                    yield return null;
                }
                progress?.Invoke(1f);
                if (request.result != UnityWebRequest.Result.Success)
                {
                    if (request.responseCode == 416 && File.Exists(partialPath))
                    {
                        var rangeFileValid = false;
                        string rangeFileError = null;
                        if (expectedSize > 0 && !string.IsNullOrWhiteSpace(expectedSha256))
                            yield return ValidateFile(partialPath, expectedSize, expectedSha256,
                                (ok, error) => { rangeFileValid = ok; rangeFileError = error; });
                        else
                            rangeFileError = "缺少完整性元数据，不能接受416断点文件。";
                        if (rangeFileValid)
                        {
                            File.Move(partialPath, finalPath);
                            RememberValidatedFile(mediaUrl, finalPath, expectedSize, expectedSha256);
                            success(new Uri(finalPath).AbsoluteUri);
                            yield break;
                        }

                        Debug.LogWarning("LED断点文件收到416且校验失败，将从头下载：" + rangeFileError);
                        File.Delete(partialPath);
                        yield return Resolve(mediaUrl, success, failure, expectedSize, progress, expectedSha256);
                        yield break;
                    }
                    failure(request.error);
                    yield break;
                }

                // Some static file servers ignore Range and return the complete file.
                // Never append a complete response to an existing partial file.
                if (existingLength > 0 && request.responseCode == 200)
                {
                    request.Dispose();
                    if (File.Exists(partialPath)) File.Delete(partialPath);
                    yield return Resolve(mediaUrl, success, failure, expectedSize, progress, expectedSha256);
                    yield break;
                }
            }

            bool downloadedFileValid = false;
            string downloadedFileError = null;
            yield return ValidateFile(partialPath, expectedSize, expectedSha256,
                (ok, error) => { downloadedFileValid = ok; downloadedFileError = error; });
            if (!downloadedFileValid)
            {
                File.Delete(partialPath);
                failure(downloadedFileError);
                yield break;
            }

            File.Move(partialPath, finalPath);
            RememberValidatedFile(mediaUrl, finalPath, expectedSize, expectedSha256);
            success(new Uri(finalPath).AbsoluteUri);
        }

        private bool CanReuseValidatedFile(string mediaUrl, string path, long expectedSize, string expectedSha256)
        {
            if (expectedSize <= 0 || string.IsNullOrWhiteSpace(expectedSha256) || expectedSha256.Length != 64 ||
                !validatedFilesByUrl.TryGetValue(mediaUrl, out var validated)) return false;
            var file = new FileInfo(path);
            return file.Exists && file.Length == expectedSize && file.Length == validated.FileSizeBytes &&
                   file.LastWriteTimeUtc.Ticks == validated.LastWriteUtcTicks &&
                   validated.ExpectedSizeBytes == expectedSize &&
                   string.Equals(validated.ExpectedSha256, NormalizeSha256(expectedSha256),
                       StringComparison.OrdinalIgnoreCase);
        }

        private void RememberValidatedFile(string mediaUrl, string path, long expectedSize, string expectedSha256)
        {
            if (expectedSize <= 0 || string.IsNullOrWhiteSpace(expectedSha256) || expectedSha256.Length != 64) return;
            var file = new FileInfo(path);
            if (!file.Exists) return;
            validatedFilesByUrl[mediaUrl] = new ValidatedFileMetadata(file.Length, file.LastWriteTimeUtc.Ticks,
                expectedSize, NormalizeSha256(expectedSha256));
        }

        private static string NormalizeSha256(string value) =>
            string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();

        private static IEnumerator ValidateFile(string path, long expectedSize, string expectedSha256,
            Action<bool, string> completed)
        {
            var task = Task.Run(() => ValidateFileSync(path, expectedSize, expectedSha256));
            while (!task.IsCompleted) yield return null;
            if (task.IsFaulted)
            {
                completed(false, task.Exception?.GetBaseException().Message ?? "缓存文件校验失败。");
                yield break;
            }
            completed(task.Result.Error == null, task.Result.Error);
        }

        private static ValidationResult ValidateFileSync(string path, long expectedSize, string expectedSha256)
        {
            var file = new FileInfo(path);
            if (!file.Exists || file.Length <= 0) return new ValidationResult("缓存文件不存在或为空。");
            if (expectedSize > 0 && file.Length != expectedSize)
                return new ValidationResult("缓存文件大小不一致：预期 " + expectedSize + " 字节，实际 " + file.Length + " 字节。");
            if (string.IsNullOrWhiteSpace(expectedSha256)) return new ValidationResult(null);
            if (expectedSha256.Length != 64) return new ValidationResult("缓存文件SHA-256格式无效。");

            using (var sha = SHA256.Create())
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024,
                       FileOptions.SequentialScan))
            {
                var actual = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
                if (!string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase))
                    return new ValidationResult("缓存文件SHA-256不一致：预期 " + expectedSha256.ToLowerInvariant() + "，实际 " + actual + "。");
            }
            return new ValidationResult(null);
        }

        private string GetContentDirectory()
        {
            if (!string.IsNullOrWhiteSpace(contentDirectory)) return contentDirectory;
            contentDirectory = Path.Combine(Application.persistentDataPath, "Content");
            Directory.CreateDirectory(contentDirectory);
            return contentDirectory;
        }

        private string GetCachePath(string mediaUrl)
        {
            var extension = Path.GetExtension(new Uri(mediaUrl).AbsolutePath);
            if (string.IsNullOrWhiteSpace(extension)) extension = ".mp4";
            return Path.Combine(GetContentDirectory(), Hash(mediaUrl) + extension);
        }

        private static string Hash(string value)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(value));
                return BitConverter.ToString(bytes).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        private readonly struct ValidationMetadata
        {
            public ValidationMetadata(long sizeBytes, string sha256)
            {
                SizeBytes = sizeBytes;
                Sha256 = sha256;
            }

            public long SizeBytes { get; }
            public string Sha256 { get; }
        }

        private readonly struct ValidatedFileMetadata
        {
            public ValidatedFileMetadata(long fileSizeBytes, long lastWriteUtcTicks, long expectedSizeBytes,
                string expectedSha256)
            {
                FileSizeBytes = fileSizeBytes;
                LastWriteUtcTicks = lastWriteUtcTicks;
                ExpectedSizeBytes = expectedSizeBytes;
                ExpectedSha256 = expectedSha256;
            }

            public long FileSizeBytes { get; }
            public long LastWriteUtcTicks { get; }
            public long ExpectedSizeBytes { get; }
            public string ExpectedSha256 { get; }
        }

        private readonly struct ValidationResult
        {
            public ValidationResult(string error) => Error = error;
            public string Error { get; }
        }
    }
}
