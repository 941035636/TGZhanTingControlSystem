using System;
using System.Collections;
using System.IO;
using TG.Control.UnityContracts;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace TG.Control.LedPlayer
{
    /// <summary>
    /// Owns only the manual gallery image surface. It deliberately does not touch AVPro,
    /// formal playback state, welcome audio, or the legacy UDP compatibility bridge.
    /// </summary>
    public sealed class LedImageDisplayController : MonoBehaviour
    {
        [SerializeField] private LedApiClient apiClient;
        [SerializeField] private float crossFadeSeconds = 0.25f;
        [SerializeField] private float maximumTrustedScheduleOffsetSeconds = 1f;
        [SerializeField] private int maximumTextureDimension = 8192;

        private readonly LedContentCache contentCache = LedContentCache.Shared;
        private readonly Image[] imageLayers = new Image[2];
        private readonly Sprite[] sprites = new Sprite[2];
        private readonly Texture2D[] textures = new Texture2D[2];
        private GameObject canvasRoot;
        private Coroutine prepareCoroutine;
        private Coroutine displayCoroutine;
        private int generation;
        private int activeIndex = -1;
        private int preparedIndex = -1;
        private int transitionFromIndex = -1;
        private int transitionTargetIndex = -1;
        private GalleryDisplayCommand preparedCommand;
        private GalleryDisplayCommand transitionCommand;
        private string activeSessionId;
        private string activeImageId;

        public event Action<bool> GalleryActiveChanged;
        public bool IsGalleryVisible => canvasRoot != null && canvasRoot.activeSelf;

        private void Awake()
        {
            BuildUi();
        }

        private void OnEnable()
        {
            if (apiClient == null) return;
            apiClient.GalleryCommandReceived += HandleGallery;
            apiClient.CommandReceived += HandleFormalPlayback;
            apiClient.WelcomeCommandReceived += HandleWelcome;
        }

        private void OnDisable()
        {
            if (apiClient != null)
            {
                apiClient.GalleryCommandReceived -= HandleGallery;
                apiClient.CommandReceived -= HandleFormalPlayback;
                apiClient.WelcomeCommandReceived -= HandleWelcome;
            }
            WithdrawForHigherPriority();
        }

        private void OnDestroy()
        {
            ReleaseBuffer(0);
            ReleaseBuffer(1);
        }

        private void BuildUi()
        {
            canvasRoot = new GameObject("LED Gallery Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            canvasRoot.transform.SetParent(transform, false);
            var canvas = canvasRoot.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = canvasRoot.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            var background = CreateImage("Gallery Black Background", canvasRoot.transform, Color.black);
            Stretch(background.rectTransform);
            for (var index = 0; index < imageLayers.Length; index++)
            {
                var layer = CreateImage("Gallery Image " + (index == 0 ? "A" : "B"), canvasRoot.transform,
                    new Color(1f, 1f, 1f, 0f));
                Stretch(layer.rectTransform);
                layer.preserveAspect = true;
                layer.type = Image.Type.Simple;
                imageLayers[index] = layer;
            }
            canvasRoot.SetActive(false);
        }

        private void HandleGallery(GalleryDisplayCommand command)
        {
            if (command == null || apiClient == null) return;
            Debug.Log($"[LED Gallery] 收到指令：action={command.action}，sessionId={command.sessionId}，" +
                      $"imageId={command.imageId}，commandId={command.commandId}，sequence={command.sequence}。");
            switch (command.action)
            {
                case GalleryDisplayAction.Prepare:
                    BeginPrepare(command, false);
                    break;
                case GalleryDisplayAction.Show:
                    if (IsCurrentImage(command))
                    {
                        apiClient.ReportGallery(command, GalleryDisplayState.Showing);
                    }
                    else if (MatchesPrepared(command))
                    {
                        BeginDisplay(command);
                    }
                    else
                    {
                        // A reconnect can deliver Show after this process lost its in-memory
                        // Prepare state. The Show envelope includes full immutable asset data.
                        BeginPrepare(command, true);
                    }
                    break;
                case GalleryDisplayAction.Stop:
                    if (BelongsToCurrentOrPreparedSession(command.sessionId))
                        StopGallery(command);
                    else
                        apiClient.ReportGallery(command, GalleryDisplayState.Stopped);
                    break;
            }
        }

        private void HandleFormalPlayback(PlaybackCommand command)
        {
            if (command.action == PlaybackAction.Prepare || command.action == PlaybackAction.PlayVideo ||
                command.action == PlaybackAction.PlayNarration)
                WithdrawForHigherPriority();
        }

        private void HandleWelcome(WelcomePlaybackCommand command)
        {
            if (command.action == WelcomePlaybackAction.Prepare || command.action == WelcomePlaybackAction.Play)
                WithdrawForHigherPriority();
        }

        private void BeginPrepare(GalleryDisplayCommand command, bool showAfterPrepare)
        {
            string validationError;
            if (!ValidateCommand(command, out validationError))
            {
                Fail(command, validationError, -1);
                return;
            }

            FinishTransitionImmediately();
            CancelPrepare();
            if (IsCurrentImage(command))
            {
                preparedCommand = command;
                preparedIndex = activeIndex;
                apiClient.ReportGallery(command, GalleryDisplayState.Received);
                if (showAfterPrepare) apiClient.ReportGallery(command, GalleryDisplayState.Showing);
                else apiClient.ReportGallery(command, GalleryDisplayState.Ready);
                return;
            }

            if (preparedIndex >= 0 && preparedIndex != activeIndex) ReleaseBuffer(preparedIndex);
            preparedIndex = activeIndex == 0 ? 1 : 0;
            preparedCommand = null;
            var activeGeneration = ++generation;
            prepareCoroutine = StartCoroutine(PrepareImage(command, preparedIndex, activeGeneration, showAfterPrepare));
        }

        private IEnumerator PrepareImage(GalleryDisplayCommand command, int targetIndex, int activeGeneration,
            bool showAfterPrepare)
        {
            apiClient.ReportGallery(command, GalleryDisplayState.Received);
            var normalizedUrl = apiClient.NormalizeUrl(command.url);
            contentCache.RegisterValidation(normalizedUrl, command.sizeBytes, command.sha256);
            string localUrl = null;
            string cacheError = null;
            yield return contentCache.Resolve(normalizedUrl, value => localUrl = value, value => cacheError = value,
                command.sizeBytes, expectedSha256: command.sha256);
            if (activeGeneration != generation) yield break;
            if (!string.IsNullOrWhiteSpace(cacheError) || string.IsNullOrWhiteSpace(localUrl))
            {
                Fail(command, "图片缓存或完整性校验失败：" + (cacheError ?? "未获得本地文件"), targetIndex);
                yield break;
            }

            using (var request = UnityWebRequestTexture.GetTexture(localUrl, false))
            {
                request.timeout = 30;
                yield return request.SendWebRequest();
                if (activeGeneration != generation) yield break;
                if (request.result != UnityWebRequest.Result.Success)
                {
                    Fail(command, "图片解码失败：" + request.error, targetIndex);
                    yield break;
                }

                var texture = DownloadHandlerTexture.GetContent(request);
                if (texture == null || texture.width <= 0 || texture.height <= 0)
                {
                    Fail(command, "图片解码结果为空。", targetIndex);
                    yield break;
                }
                if (texture.width > maximumTextureDimension || texture.height > maximumTextureDimension)
                {
                    var width = texture.width;
                    var height = texture.height;
                    Destroy(texture);
                    Fail(command, $"图片尺寸 {width}×{height} 超过播放端上限。", targetIndex);
                    yield break;
                }

                ReleaseBuffer(targetIndex);
                textures[targetIndex] = texture;
                sprites[targetIndex] = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f), 100f);
                imageLayers[targetIndex].sprite = sprites[targetIndex];
                SetLayerAlpha(targetIndex, 0f);
                preparedIndex = targetIndex;
                preparedCommand = command;
                prepareCoroutine = null;
                if (showAfterPrepare) BeginDisplay(command);
                else apiClient.ReportGallery(command, GalleryDisplayState.Ready);
            }
        }

        private void BeginDisplay(GalleryDisplayCommand command)
        {
            if (!MatchesPrepared(command))
            {
                BeginPrepare(command, true);
                return;
            }
            if (displayCoroutine != null) FinishTransitionImmediately();
            transitionFromIndex = activeIndex;
            transitionTargetIndex = preparedIndex;
            transitionCommand = command;
            displayCoroutine = StartCoroutine(DisplayPrepared(command, transitionFromIndex,
                transitionTargetIndex, generation));
        }

        private IEnumerator DisplayPrepared(GalleryDisplayCommand command, int fromIndex, int targetIndex,
            int activeGeneration)
        {
            if (DateTimeOffset.TryParse(command.executeAtUtc, out var executeAt))
            {
                var delay = (executeAt - DateTimeOffset.UtcNow).TotalSeconds;
                if (delay > 0 && delay <= maximumTrustedScheduleOffsetSeconds)
                    yield return new WaitForSecondsRealtime((float)delay);
            }
            if (activeGeneration != generation) yield break;

            SetGalleryVisible(true);
            SetLayerAlpha(targetIndex, 0f);
            if (fromIndex >= 0) SetLayerAlpha(fromIndex, 1f);
            var duration = Mathf.Max(0.05f, crossFadeSeconds);
            var startedAt = Time.realtimeSinceStartup;
            while (activeGeneration == generation)
            {
                var progress = Mathf.Clamp01((Time.realtimeSinceStartup - startedAt) / duration);
                SetLayerAlpha(targetIndex, progress);
                if (fromIndex >= 0 && fromIndex != targetIndex) SetLayerAlpha(fromIndex, 1f - progress);
                if (progress >= 1f) break;
                yield return null;
            }
            if (activeGeneration != generation) yield break;

            CommitTransition(command, fromIndex, targetIndex);
            displayCoroutine = null;
            apiClient.ReportGallery(command, GalleryDisplayState.Showing);
        }

        private void CommitTransition(GalleryDisplayCommand command, int fromIndex, int targetIndex)
        {
            SetLayerAlpha(targetIndex, 1f);
            if (fromIndex >= 0 && fromIndex != targetIndex) ReleaseBuffer(fromIndex);
            activeIndex = targetIndex;
            activeSessionId = command.sessionId;
            activeImageId = command.imageId;
            preparedIndex = -1;
            preparedCommand = null;
            transitionFromIndex = -1;
            transitionTargetIndex = -1;
            transitionCommand = null;
        }

        private void FinishTransitionImmediately()
        {
            if (displayCoroutine == null || transitionCommand == null || transitionTargetIndex < 0) return;
            StopCoroutine(displayCoroutine);
            displayCoroutine = null;
            CommitTransition(transitionCommand, transitionFromIndex, transitionTargetIndex);
        }

        private void StopGallery(GalleryDisplayCommand command)
        {
            apiClient.ReportGallery(command, GalleryDisplayState.Stopped);
            generation++;
            CancelPrepare();
            if (displayCoroutine != null) StopCoroutine(displayCoroutine);
            displayCoroutine = null;
            transitionCommand = null;
            transitionFromIndex = -1;
            transitionTargetIndex = -1;
            preparedCommand = null;
            preparedIndex = -1;
            activeSessionId = null;
            activeImageId = null;
            activeIndex = -1;
            ReleaseBuffer(0);
            ReleaseBuffer(1);
            SetGalleryVisible(false);
        }

        private void WithdrawForHigherPriority()
        {
            if (canvasRoot == null) return;
            generation++;
            CancelPrepare();
            if (displayCoroutine != null) StopCoroutine(displayCoroutine);
            displayCoroutine = null;
            transitionCommand = null;
            transitionFromIndex = -1;
            transitionTargetIndex = -1;
            preparedCommand = null;
            preparedIndex = -1;
            activeSessionId = null;
            activeImageId = null;
            activeIndex = -1;
            ReleaseBuffer(0);
            ReleaseBuffer(1);
            SetGalleryVisible(false);
        }

        private void CancelPrepare()
        {
            if (prepareCoroutine == null) return;
            StopCoroutine(prepareCoroutine);
            prepareCoroutine = null;
        }

        private void Fail(GalleryDisplayCommand command, string error, int failedBufferIndex)
        {
            if (failedBufferIndex >= 0 && failedBufferIndex != activeIndex) ReleaseBuffer(failedBufferIndex);
            preparedIndex = -1;
            preparedCommand = null;
            prepareCoroutine = null;
            Debug.LogError("[LED Gallery] " + error);
            apiClient.ReportGallery(command, GalleryDisplayState.Failed, error);
            // Keep the previous valid image visible. If this was the first image,
            // the idle surface remains visible because Gallery has never been activated.
        }

        private bool ValidateCommand(GalleryDisplayCommand command, out string error)
        {
            if (string.IsNullOrWhiteSpace(command.sessionId) || string.IsNullOrWhiteSpace(command.moduleId) ||
                string.IsNullOrWhiteSpace(command.imageId) || string.IsNullOrWhiteSpace(command.assetId) ||
                string.IsNullOrWhiteSpace(command.url))
            {
                error = "图片指令缺少会话或资产身份。";
                return false;
            }
            if (command.sizeBytes <= 0 || string.IsNullOrWhiteSpace(command.sha256) || command.sha256.Length != 64)
            {
                error = "图片指令缺少有效的Size/SHA-256完整性信息。";
                return false;
            }
            if (!string.Equals(command.mediaType, "image/jpeg", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(command.mediaType, "image/png", StringComparison.OrdinalIgnoreCase))
            {
                error = "播放端仅支持正式发布的JPG/JPEG和PNG图集图片。";
                return false;
            }
            error = null;
            return true;
        }

        private bool MatchesPrepared(GalleryDisplayCommand command) => preparedCommand != null &&
            preparedIndex >= 0 && sprites[preparedIndex] != null &&
            string.Equals(preparedCommand.sessionId, command.sessionId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(preparedCommand.imageId, command.imageId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(preparedCommand.assetId, command.assetId, StringComparison.OrdinalIgnoreCase);

        private bool IsCurrentImage(GalleryDisplayCommand command) => activeIndex >= 0 &&
            string.Equals(activeSessionId, command.sessionId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(activeImageId, command.imageId, StringComparison.OrdinalIgnoreCase);

        private bool BelongsToCurrentOrPreparedSession(string sessionId) =>
            (string.IsNullOrWhiteSpace(activeSessionId) && preparedCommand == null) ||
            string.Equals(activeSessionId, sessionId, StringComparison.OrdinalIgnoreCase) ||
            (preparedCommand != null &&
             string.Equals(preparedCommand.sessionId, sessionId, StringComparison.OrdinalIgnoreCase));

        private void SetGalleryVisible(bool visible)
        {
            if (canvasRoot.activeSelf == visible) return;
            canvasRoot.SetActive(visible);
            GalleryActiveChanged?.Invoke(visible);
        }

        private void ReleaseBuffer(int index)
        {
            if (index < 0 || index >= imageLayers.Length) return;
            if (imageLayers[index] != null) imageLayers[index].sprite = null;
            if (sprites[index] != null) Destroy(sprites[index]);
            if (textures[index] != null) Destroy(textures[index]);
            sprites[index] = null;
            textures[index] = null;
            SetLayerAlpha(index, 0f);
        }

        private void SetLayerAlpha(int index, float alpha)
        {
            if (index < 0 || index >= imageLayers.Length || imageLayers[index] == null) return;
            imageLayers[index].color = new Color(1f, 1f, 1f, Mathf.Clamp01(alpha));
        }

        private static Image CreateImage(string name, Transform parent, Color color)
        {
            var image = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            image.transform.SetParent(parent, false);
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
