using System;
using System.Collections;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace TG.Control.LedPlayer
{
    public sealed class LedContentCache
    {
        private string contentDirectory;

        public IEnumerator Resolve(string mediaUrl, Action<string> success, Action<string> failure)
        {
            if (string.IsNullOrWhiteSpace(mediaUrl))
            {
                failure("媒体URL为空。");
                yield break;
            }

            if (mediaUrl.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            {
                success(mediaUrl);
                yield break;
            }

            if (Path.IsPathRooted(mediaUrl))
            {
                success(new Uri(mediaUrl).AbsoluteUri);
                yield break;
            }

            var directory = GetContentDirectory();
            var extension = Path.GetExtension(new Uri(mediaUrl).AbsolutePath);
            if (string.IsNullOrWhiteSpace(extension)) extension = ".mp4";
            var finalPath = Path.Combine(directory, Hash(mediaUrl) + extension);
            if (File.Exists(finalPath) && new FileInfo(finalPath).Length > 0)
            {
                success(new Uri(finalPath).AbsoluteUri);
                yield break;
            }

            var partialPath = finalPath + ".partial";
            var existingLength = File.Exists(partialPath) ? new FileInfo(partialPath).Length : 0;
            using (var request = UnityWebRequest.Get(mediaUrl))
            {
                request.downloadHandler = new DownloadHandlerFile(partialPath, existingLength > 0);
                if (existingLength > 0) request.SetRequestHeader("Range", "bytes=" + existingLength + "-");
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                {
                    failure(request.error);
                    yield break;
                }
            }

            File.Move(partialPath, finalPath);
            success(new Uri(finalPath).AbsoluteUri);
        }

        private string GetContentDirectory()
        {
            if (!string.IsNullOrWhiteSpace(contentDirectory)) return contentDirectory;
            contentDirectory = Path.Combine(Application.persistentDataPath, "Content");
            Directory.CreateDirectory(contentDirectory);
            return contentDirectory;
        }

        private static string Hash(string value)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(value));
                return BitConverter.ToString(bytes).Replace("-", string.Empty).ToLowerInvariant();
            }
        }
    }
}
