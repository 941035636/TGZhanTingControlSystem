using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace TG.Control.Touch.UI.Services
{
    /// <summary>
    /// Shared runtime image cache for configurable hero and module covers.
    /// It never knows about TouchApiClient and deduplicates concurrent downloads.
    /// </summary>
    public sealed class TouchImageLoader
    {
        private const float FailureRetrySeconds = 30;
        private readonly MonoBehaviour coroutineHost;
        private readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<Action<Sprite>>> pending = new Dictionary<string, List<Action<Sprite>>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, float> failedAt = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

        public TouchImageLoader(MonoBehaviour coroutineHost)
        {
            this.coroutineHost = coroutineHost ?? throw new ArgumentNullException(nameof(coroutineHost));
        }

        public void Load(Image target, string url, Action<bool> completed = null)
        {
            if (target == null || string.IsNullOrWhiteSpace(url))
            {
                completed?.Invoke(false);
                return;
            }

            if (cache.TryGetValue(url, out var cached) && cached != null)
            {
                Apply(target, cached);
                completed?.Invoke(true);
                return;
            }

            if (failedAt.TryGetValue(url, out var failureTime) && Time.realtimeSinceStartup - failureTime < FailureRetrySeconds)
            {
                completed?.Invoke(false);
                return;
            }

            Action<Sprite> callback = sprite =>
            {
                if (target != null && sprite != null) Apply(target, sprite);
                completed?.Invoke(sprite != null);
            };
            if (pending.TryGetValue(url, out var callbacks))
            {
                callbacks.Add(callback);
                return;
            }

            pending[url] = new List<Action<Sprite>> { callback };
            coroutineHost.StartCoroutine(Download(url));
        }

        private IEnumerator Download(string url)
        {
            Sprite sprite = null;
            using (var request = UnityWebRequestTexture.GetTexture(url))
            {
                yield return request.SendWebRequest();
                if (request.result == UnityWebRequest.Result.Success)
                {
                    var texture = DownloadHandlerTexture.GetContent(request);
                    if (texture != null)
                    {
                        texture.name = "TG UI " + url;
                        texture.wrapMode = TextureWrapMode.Clamp;
                        texture.filterMode = FilterMode.Bilinear;
                        sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                            new Vector2(.5f, .5f), 100);
                        sprite.name = texture.name;
                        cache[url] = sprite;
                        failedAt.Remove(url);
                    }
                }
            }

            if (sprite == null) failedAt[url] = Time.realtimeSinceStartup;
            if (!pending.TryGetValue(url, out var callbacks)) yield break;
            pending.Remove(url);
            foreach (var callback in callbacks) callback?.Invoke(sprite);
        }

        private static void Apply(Image target, Sprite sprite)
        {
            target.sprite = sprite;
            target.type = Image.Type.Simple;
            target.preserveAspect = false;
            target.color = Color.white;
        }
    }
}
