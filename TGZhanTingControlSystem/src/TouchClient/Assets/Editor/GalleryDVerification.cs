using System;
using System.IO;
using System.Linq;
using TG.Control.Touch.UI;
using TG.Control.Touch.UI.Components;
using TG.Control.Touch.UI.Pages;
using TG.Control.Touch.UI.Services;
using TG.Control.Touch.UI.Theme;
using TG.Control.UnityContracts;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace TG.Control.Editor
{
    public static class GalleryDVerification
    {
        private sealed class CoroutineHost : MonoBehaviour { }

        public static void Run()
        {
            var result = "FAIL";
            var detail = string.Empty;
            GameObject root = null;
            TouchImageLoader loader = null;
            try
            {
                root = new GameObject("Gallery D Verification Root", typeof(RectTransform), typeof(Canvas));
                var host = root.AddComponent<CoroutineHost>();
                var theme = TouchTheme.CreateDefault();
                var factory = new TouchUiFactory(theme.CreateFont(), () => theme.Primary, theme);
                loader = new TouchImageLoader(host);
                var page = new ImageGalleryPage(factory, theme, loader, root.transform);

                var content = new PublishedContent
                {
                    version = 12,
                    modules = new[]
                    {
                        new ExhibitionModule
                        {
                            id = "module-a", name = "一路向新", enabled = true, order = 1,
                            galleryImages = new[]
                            {
                                Asset("image-a", "发展起点"),
                                Asset("image-b", "创新成果"),
                                Asset("image-c", "未来规划")
                            }
                        },
                        new ExhibitionModule
                        {
                            id = "module-empty", name = "暂无图集", enabled = true, order = 2,
                            galleryImages = Array.Empty<ContentAsset>()
                        }
                    }
                };
                var state = new TouchUiState();
                SetState(state, nameof(TouchUiState.Content), content);
                SetState(state, nameof(TouchUiState.Connected), true);
                SetState(state, nameof(TouchUiState.Readiness),
                    new SystemReadiness { ledOnline = true, ledReady = true, canStart = true });

                string displayedModule = null;
                var displayedIndex = -1;
                var nextRequests = 0;
                page.DisplayRequested += (moduleId, imageIndex) =>
                {
                    displayedModule = moduleId;
                    displayedIndex = imageIndex;
                };
                page.NextRequested += () => nextRequests++;
                page.Render(state, value => value);

                Require(Find(root.transform, "Gallery Module - 一路向新") != null,
                    "module with gallery was not rendered");
                Require(Find(root.transform, "Gallery Module - 暂无图集") == null,
                    "empty module must not be rendered in gallery navigation");
                var secondThumbnail = Find(root.transform, "Gallery Thumbnail 2")?.GetComponent<Button>();
                Require(secondThumbnail != null, "second thumbnail button was not rendered");
                secondThumbnail.onClick.Invoke();
                Require(displayedIndex == -1, "thumbnail preview changed the LED without explicit display");

                var display = Find(root.transform, "Product Primary - 投放到大屏")?.GetComponent<Button>();
                Require(display != null && display.interactable, "display button should be enabled");
                display.onClick.Invoke();
                Require(displayedModule == "module-a" && displayedIndex == 1,
                    "explicit display did not emit the selected module/image");

                SetState(state, nameof(TouchUiState.HasActiveGallery), true);
                SetState(state, nameof(TouchUiState.Gallery), new GallerySessionStatus
                {
                    sessionId = "gallery-session", moduleId = "module-a", moduleName = "一路向新",
                    imageId = "image-b", imageName = "创新成果", currentImageNumber = 2, imageCount = 3,
                    state = GallerySessionState.Showing, slideshowActive = false, slideshowIntervalSeconds = 8
                });
                page.Render(state, value => value);
                var next = Find(root.transform, "Product Secondary - 下一张")?.GetComponent<Button>();
                Require(next != null && next.interactable, "next button should be enabled for active gallery");
                next.onClick.Invoke();
                Require(nextRequests == 1, "next action was not emitted");

                var navigation = new SideNavigation(factory, theme, root.transform);
                Require(Find(navigation.Root, "Navigation - 图片展示") != null,
                    "gallery navigation entry was not built");

                var pageSource = File.ReadAllText(Path.Combine(Application.dataPath,
                    "Scripts", "UI", "Pages", "ImageGalleryPage.cs"));
                Require(!pageSource.Contains("TouchApiClient"),
                    "ImageGalleryPage must not call TouchApiClient directly");
                Require(pageSource.Contains("DisplayRequested") && pageSource.Contains("StopRequested"),
                    "gallery page intent events are incomplete");

                result = "PASS";
                detail = "module filtering, preview-only thumbnail, explicit display, active controls, navigation and API boundary passed";
            }
            catch (Exception exception)
            {
                detail = exception.ToString();
                throw;
            }
            finally
            {
                loader?.Dispose();
                if (root != null) UnityEngine.Object.DestroyImmediate(root);
                WriteResult(result, detail);
            }
        }

        private static ContentAsset Asset(string id, string name) => new ContentAsset
        {
            id = id,
            name = name,
            kind = 1,
            url = string.Empty,
            sha256 = new string('a', 64),
            sizeBytes = 1024
        };

        private static Transform Find(Transform root, string name)
        {
            if (root.name == name) return root;
            return root.Cast<Transform>().Select(child => Find(child, name)).FirstOrDefault(found => found != null);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private static void SetState(TouchUiState state, string property, object value)
        {
            var target = typeof(TouchUiState).GetProperty(property);
            if (target == null) throw new MissingMemberException(typeof(TouchUiState).FullName, property);
            target.SetValue(state, value);
        }

        private static void WriteResult(string status, string detail)
        {
            var repository = Directory.GetParent(Application.dataPath)?.Parent?.Parent?.FullName;
            if (string.IsNullOrWhiteSpace(repository)) return;
            var directory = Path.Combine(repository, "artifacts");
            Directory.CreateDirectory(directory);
            var escaped = (detail ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"")
                .Replace("\r", "\\r").Replace("\n", "\\n");
            File.WriteAllText(Path.Combine(directory, "gallery-d-editor-verification.json"),
                "{\n  \"status\": \"" + status + "\",\n  \"detail\": \"" + escaped + "\"\n}\n");
        }
    }
}
