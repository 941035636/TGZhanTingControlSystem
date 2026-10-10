using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace TG.Control.Touch.UI.Theme
{
    /// <summary>
    /// Loads and caches the Phase UI-12 home-only visual assets. Keeping this in one place prevents
    /// pages from repeatedly creating sprites and makes the visual mode reversible for non-home pages.
    /// </summary>
    public static class HomeDarkVisualAssets
    {
        private const string Root = "TGExhibitionUI/HomeDarkV2/";
        private static readonly Dictionary<string, Sprite> RuntimeSprites = new Dictionary<string, Sprite>();

        public static Sprite HallBackground => Load("Backgrounds/Home_Hall_1920x1080", 0);
        public static Sprite ContentGlass => Load("Panels/Content_Glass_9slice", 24);
        public static Sprite TitleGlass => Load("Panels/Title_Glass_9slice", 24);
        public static Sprite BottomGlass => Load("Panels/Bottom_Glass_9slice", 24);
        public static Sprite SidebarGlass => Load("Panels/Sidebar_Glass_9slice", 24);
        public static Sprite NavigationIcon(string name, bool selected) =>
            Load("Icons/nav_" + name + (selected ? "_selected" : "_normal"), 0);

        public static void ApplySliced(Image image, Sprite sprite, Color tint)
        {
            if (image == null || sprite == null) return;
            image.sprite = sprite;
            image.type = Image.Type.Sliced;
            image.fillCenter = true;
            image.color = tint;
        }

        private static Sprite Load(string relativePath, float border)
        {
            var resourcePath = Root + relativePath;
            var imported = Resources.Load<Sprite>(resourcePath);
            if (imported != null) return imported;

            Sprite cached;
            if (RuntimeSprites.TryGetValue(resourcePath, out cached)) return cached;

            var texture = Resources.Load<Texture2D>(resourcePath);
            if (texture == null) return null;
            var edge = Mathf.Min(border, Mathf.Min(texture.width, texture.height) * .25f);
            cached = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                new Vector2(.5f, .5f), 100f, 0, SpriteMeshType.FullRect,
                new Vector4(edge, edge, edge, edge));
            cached.name = "TG UI-12 " + relativePath;
            cached.hideFlags = HideFlags.HideAndDontSave;
            RuntimeSprites[resourcePath] = cached;
            return cached;
        }
    }
}
