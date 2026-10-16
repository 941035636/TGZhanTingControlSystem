using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using TG.Control.UnityContracts;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace TG.Control.LedPlayer.Editor
{
    /// <summary>
    /// Batch-mode verification for the Gallery C rendering boundary. This intentionally
    /// exercises only deterministic presentation and integrity rules; live Server/LED
    /// command flow remains an integration acceptance item.
    /// </summary>
    public static class GalleryCVerification
    {
        public static void Run()
        {
            var temporaryFile = Path.Combine(Path.GetTempPath(), "tg-gallery-c-" + Guid.NewGuid().ToString("N") + ".png");
            GameObject root = null;
            try
            {
                root = new GameObject("Gallery C Verification");
                var controller = root.AddComponent<LedImageDisplayController>();
                // MonoBehaviour.Awake is not invoked for a plain AddComponent call in
                // batch edit mode, so explicitly construct the runtime-owned surface.
                GetMethod(typeof(LedImageDisplayController), "BuildUi").Invoke(controller, null);
                VerifyCanvas(controller);
                VerifyCommandValidation(controller);
                VerifyPriorityWithdrawal(controller);
                VerifyIntegrityValidation(temporaryFile);
                Debug.Log("GALLERY_C_VERIFICATION_PASS");
            }
            finally
            {
                if (root != null) UnityEngine.Object.DestroyImmediate(root);
                if (File.Exists(temporaryFile)) File.Delete(temporaryFile);
            }
        }

        private static void VerifyCanvas(LedImageDisplayController controller)
        {
            var canvas = controller.GetComponentInChildren<Canvas>(true);
            Require(canvas != null, "Gallery Canvas was not created.");
            Require(canvas.sortingOrder == 10, "Gallery Canvas must remain between video (0) and idle overlay (20).");
            Require(!canvas.gameObject.activeSelf, "Gallery Canvas must start hidden so the idle surface remains visible.");

            var images = canvas.GetComponentsInChildren<Image>(true);
            Require(images.Length == 3, "Gallery Canvas must contain one black background and two image buffers.");
            Require(images.All(image => !image.raycastTarget), "Gallery decoration must never intercept input.");
            var buffers = images.Where(image => image.name.StartsWith("Gallery Image", StringComparison.Ordinal)).ToArray();
            Require(buffers.Length == 2, "Gallery must own exactly two image buffers.");
            Require(buffers.All(image => image.preserveAspect && image.type == Image.Type.Simple),
                "Both image buffers must use Aspect Fit semantics.");
        }

        private static void VerifyCommandValidation(LedImageDisplayController controller)
        {
            var valid = ValidCommand();
            Require(Validate(controller, valid, out var error), "Valid JPEG command was rejected: " + error);

            var missingIntegrity = ValidCommand();
            missingIntegrity.sha256 = string.Empty;
            Require(!Validate(controller, missingIntegrity, out error) && error.Contains("SHA-256"),
                "Missing SHA-256 must be rejected.");

            var unsupported = ValidCommand();
            unsupported.mediaType = "image/webp";
            Require(!Validate(controller, unsupported, out error) && error.Contains("JPG/JPEG"),
                "Unsupported image media type must be rejected.");
        }

        private static void VerifyPriorityWithdrawal(LedImageDisplayController controller)
        {
            Invoke(controller, "SetGalleryVisible", true);
            Require(controller.IsGalleryVisible, "Gallery visibility test setup failed.");
            Invoke(controller, "HandleFormalPlayback", new PlaybackCommand { action = PlaybackAction.Prepare });
            Require(!controller.IsGalleryVisible, "Formal playback Prepare must immediately withdraw the gallery surface.");

            Invoke(controller, "SetGalleryVisible", true);
            Invoke(controller, "HandleWelcome", new WelcomePlaybackCommand { action = WelcomePlaybackAction.Prepare });
            Require(!controller.IsGalleryVisible, "Welcome Prepare must immediately withdraw the gallery surface.");
        }

        private static void VerifyIntegrityValidation(string path)
        {
            var bytes = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10, 1, 2, 3, 4 };
            File.WriteAllBytes(path, bytes);
            var hash = Sha256(bytes);
            Require(GetValidationError(path, bytes.Length, hash) == null,
                "A file with matching Size/SHA-256 must pass integrity validation.");
            Require(GetValidationError(path, bytes.Length + 1, hash)?.Contains("大小不一致") == true,
                "Size mismatch must fail integrity validation.");
            Require(GetValidationError(path, bytes.Length, new string('0', 64))?.Contains("SHA-256不一致") == true,
                "SHA-256 mismatch must fail integrity validation.");
        }

        private static GalleryDisplayCommand ValidCommand() => new GalleryDisplayCommand
        {
            commandId = "command-1",
            sessionId = "session-1",
            moduleId = "module-1",
            imageId = "image-1",
            assetId = "asset-1",
            url = "http://127.0.0.1:5080/media/image.png",
            sha256 = new string('a', 64),
            sizeBytes = 128,
            mediaType = "image/png"
        };

        private static bool Validate(LedImageDisplayController controller, GalleryDisplayCommand command,
            out string error)
        {
            var parameters = new object[] { command, null };
            var result = (bool)GetMethod(typeof(LedImageDisplayController), "ValidateCommand").Invoke(controller, parameters);
            error = parameters[1] as string;
            return result;
        }

        private static string GetValidationError(string path, long expectedSize, string expectedSha256)
        {
            var result = GetMethod(typeof(LedContentCache), "ValidateFileSync", true)
                .Invoke(null, new object[] { path, expectedSize, expectedSha256 });
            return result.GetType().GetProperty("Error", BindingFlags.Instance | BindingFlags.Public)
                ?.GetValue(result) as string;
        }

        private static void Invoke(object target, string methodName, object argument) =>
            GetMethod(target.GetType(), methodName).Invoke(target, new[] { argument });

        private static MethodInfo GetMethod(Type type, string methodName, bool isStatic = false) =>
            type.GetMethod(methodName, BindingFlags.NonPublic | (isStatic ? BindingFlags.Static : BindingFlags.Instance))
            ?? throw new MissingMethodException(type.FullName, methodName);

        private static string Sha256(byte[] bytes)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", string.Empty).ToLowerInvariant();
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
