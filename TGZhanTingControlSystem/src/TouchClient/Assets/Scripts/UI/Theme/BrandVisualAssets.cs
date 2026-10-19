using UnityEngine;

namespace TG.Control.Touch.UI.Theme
{
    /// <summary>Shared local brand artwork used by the standby page and application shell.</summary>
    public static class BrandVisualAssets
    {
        private const string LogoResourcePath = "logo/logo";
        private static Sprite logo;

        public static Sprite Logo
        {
            get
            {
                if (logo != null) return logo;
                var texture = Resources.Load<Texture2D>(LogoResourcePath);
                if (texture == null) return null;

                // The supplied artwork has intentionally transparent horizontal margins.
                // Crop only those margins so the tall emblem remains legible in compact headers.
                var crop = new Rect(264, 3, 732, 1243);
                if (texture.width < crop.xMax || texture.height < crop.yMax)
                    crop = new Rect(0, 0, texture.width, texture.height);
                logo = Sprite.Create(texture, crop, new Vector2(.5f, .5f), 100);
                logo.name = "TG Exhibition Brand Logo";
                return logo;
            }
        }
    }
}
