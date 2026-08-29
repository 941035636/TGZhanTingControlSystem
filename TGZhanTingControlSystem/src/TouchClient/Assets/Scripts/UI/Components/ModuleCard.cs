using System;
using TG.Control.Touch.UI.Services;
using TG.Control.Touch.UI.Theme;
using TG.Control.UnityContracts;
using UnityEngine;
using UnityEngine.UI;

namespace TG.Control.Touch.UI.Components
{
    /// <summary>Large touch-first module card for the route composer.</summary>
    public sealed class ModuleCard
    {
        private readonly TouchTheme theme;
        private readonly Image frame;
        private readonly Image surface;
        private readonly Image cover;
        private readonly Image accent;
        private readonly Text placeholder;
        private readonly Text hint;
        private readonly bool selected;

        public RectTransform Root => frame.rectTransform;

        public ModuleCard(TouchUiFactory factory, TouchTheme theme, TouchImageLoader imageLoader,
            Transform parent, ExhibitionModule module, int selectionOrder, bool configured,
            string coverUrl, bool interactable, Action<string> clicked)
        {
            this.theme = theme;
            selected = selectionOrder > 0;
            frame = factory.RoundedImage("Module Card - " + module.name, parent,
                selected ? theme.Primary : theme.Border);
            frame.gameObject.AddComponent<LayoutElement>().preferredHeight = theme.RouteEditorModuleCellSize.y;
            surface = factory.RoundedImage("Module Card Surface", frame.transform,
                selected ? theme.PrimarySoft : theme.SurfaceSoft);
            TouchUiFactory.Stretch(surface.rectTransform, 2, 2, -2, -2);

            var button = frame.gameObject.AddComponent<Button>();
            button.targetGraphic = frame;
            button.interactable = interactable;
            button.onClick.AddListener(() => clicked?.Invoke(module.id));
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = Color.Lerp(Color.white, theme.PrimaryHighlight, .12f);
            colors.pressedColor = Color.Lerp(Color.white, theme.PrimaryPressed, .22f);
            colors.disabledColor = theme.DisabledControlTint;
            colors.fadeDuration = .08f;
            button.colors = colors;

            accent = factory.RoundedImage("Module Accent", surface.transform,
                selected ? theme.Primary : Color.clear);
            TouchUiFactory.Anchor(accent.rectTransform, 0, 1, 1, 1, 0, -5, 0, 0);
            accent.raycastTarget = false;

            cover = factory.RoundedImage("Module Cover", surface.transform, theme.PrimarySoft);
            TouchUiFactory.Anchor(cover.rectTransform, 0, .5f, 0, .5f, 14, -46, 102, 42);
            cover.raycastTarget = false;
            placeholder = factory.Label("Module Cover Placeholder", cover.transform, module.order.ToString("00"),
                theme.H2, FontStyle.Bold, theme.ConfigurableAccent, TextAnchor.MiddleCenter);
            TouchUiFactory.Stretch(placeholder.rectTransform);

            var title = factory.Label("Module Name", surface.transform,
                string.IsNullOrWhiteSpace(module.name) ? "未命名主题" : module.name,
                theme.Body, FontStyle.Bold, theme.TextPrimary, TextAnchor.MiddleLeft);
            TouchUiFactory.Anchor(title.rectTransform, 0, 1, 1, 1, 116, -66, -16, -15);
            title.resizeTextForBestFit = true;
            title.resizeTextMinSize = theme.Secondary;
            title.resizeTextMaxSize = theme.Body;

            var stateText = configured ? "讲解内容就绪" : "内容待配置";
            var state = factory.Label("Module Content State", surface.transform, stateText, theme.Caption,
                FontStyle.Normal, configured ? theme.TextSecondary : theme.Warning, TextAnchor.UpperLeft);
            TouchUiFactory.Anchor(state.rectTransform, 0, 1, 1, 1, 116, -98, -16, -70);

            var badge = factory.RoundedImage("Module Selection Badge", surface.transform,
                selected ? theme.Primary : theme.SecondaryButton);
            TouchUiFactory.Anchor(badge.rectTransform, 0, 0, 0, 0, 14, 12, 102, 50);
            var orderText = factory.Label("Module Selection Order", badge.transform,
                selected ? "顺序 " + selectionOrder.ToString("00") : "未选择",
                theme.Caption, FontStyle.Bold, selected ? theme.Background : theme.TextSecondary,
                TextAnchor.MiddleCenter);
            TouchUiFactory.Stretch(orderText.rectTransform, 6, 2, -6, -2);

            hint = factory.Label("Module Touch Hint", surface.transform,
                selected ? "点击卡片移出路线" : "点击卡片加入路线", theme.Caption,
                FontStyle.Bold, selected ? theme.Primary : theme.TextMuted,
                TextAnchor.MiddleRight);
            TouchUiFactory.Anchor(hint.rectTransform, 0, 0, 1, 0, 112, 12, -16, 50);

            if (!string.IsNullOrWhiteSpace(coverUrl))
                imageLoader.Load(cover, coverUrl, success =>
                {
                    if (placeholder != null) placeholder.gameObject.SetActive(!success);
                    if (!success && cover != null) cover.color = theme.PrimarySoft;
                });
        }

        public void RefreshTheme()
        {
            frame.color = selected ? theme.Primary : theme.Border;
            accent.color = selected ? theme.Primary : Color.clear;
            hint.color = selected ? theme.Primary : theme.TextMuted;
            if (placeholder != null)
            {
                placeholder.color = theme.ConfigurableAccent;
                if (cover.sprite == null) cover.color = theme.PrimarySoft;
            }
        }
    }
}
