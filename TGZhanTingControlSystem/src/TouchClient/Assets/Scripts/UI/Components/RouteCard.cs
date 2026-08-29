using System;
using TG.Control.Touch.UI.Services;
using TG.Control.Touch.UI.Theme;
using TG.Control.UnityContracts;
using UnityEngine;
using UnityEngine.UI;

namespace TG.Control.Touch.UI.Components
{
    /// <summary>Commercial saved-route card. It renders data and emits intent only.</summary>
    public sealed class RouteCard
    {
        private readonly TouchTheme theme;
        private readonly Image frame;
        private readonly Image surface;
        private readonly Image accentBar;
        private readonly Image cover;
        private readonly Text placeholder;
        private readonly Button startButton;

        public RectTransform Root => frame.rectTransform;
        public event Action<NarrationRoute> EditRequested;
        public event Action<NarrationRoute> StartRequested;

        public RouteCard(TouchUiFactory factory, TouchTheme theme, TouchImageLoader imageLoader,
            Transform parent, NarrationRoute route, string[] moduleNames, string coverUrl,
            bool canStart, bool hasActiveSession, int index)
        {
            this.theme = theme;
            frame = factory.RoundedImage("Route Card - " + route.name, parent, theme.Border);
            frame.gameObject.AddComponent<LayoutElement>().preferredHeight = theme.RouteGridCellSize.y;
            surface = factory.RoundedImage("Route Card Surface", frame.transform, theme.SurfaceSoft);
            TouchUiFactory.Stretch(surface.rectTransform, 1, 1, -1, -1);

            accentBar = factory.RoundedImage("Configurable Accent", surface.transform, theme.ConfigurableAccent);
            TouchUiFactory.Anchor(accentBar.rectTransform, 0, 1, 1, 1, 0, -5, 0, 0);

            cover = factory.RoundedImage("Route Cover", surface.transform, theme.PrimarySoft);
            TouchUiFactory.Anchor(cover.rectTransform, 0, 1, 0, 1,
                theme.Space16, -94, 108, -theme.Space16);
            cover.raycastTarget = false;
            placeholder = factory.Label("Route Cover Placeholder", cover.transform, (index + 1).ToString("00"),
                theme.H2, FontStyle.Bold, theme.ConfigurableAccent, TextAnchor.MiddleCenter);
            TouchUiFactory.Stretch(placeholder.rectTransform);

            var title = factory.Label("Route Name", surface.transform,
                string.IsNullOrWhiteSpace(route.name) ? "未命名路线" : route.name,
                theme.CardTitle, FontStyle.Bold, theme.TextPrimary, TextAnchor.MiddleLeft);
            TouchUiFactory.Anchor(title.rectTransform, 0, 1, 1, 1,
                126, -48, -theme.Space16, -theme.Space16);
            title.resizeTextForBestFit = true;
            title.resizeTextMinSize = theme.Secondary;
            title.resizeTextMaxSize = theme.CardTitle;

            var ids = route.moduleIds ?? Array.Empty<string>();
            var count = factory.Label("Theme Count", surface.transform, ids.Length + " 个主题",
                theme.Caption, FontStyle.Bold, theme.ConfigurableAccent, TextAnchor.MiddleLeft);
            TouchUiFactory.Anchor(count.rectTransform, 0, 1, 1, 1,
                126, -74, -theme.Space16, -50);
            var summaryText = moduleNames == null || moduleNames.Length == 0
                ? "内容尚未就绪"
                : string.Join(" · ", moduleNames);
            var summary = factory.Label("Theme Summary", surface.transform, summaryText, theme.Caption,
                FontStyle.Normal, theme.TextSecondary, TextAnchor.UpperLeft);
            TouchUiFactory.Anchor(summary.rectTransform, 0, 1, 1, 1,
                126, -116, -theme.Space16, -76);

            var editButton = factory.TouchButton(surface.transform, "编辑路线", false,
                () => EditRequested?.Invoke(route));
            TouchUiFactory.Anchor(editButton.GetComponent<RectTransform>(), 0, 0, 0, 0,
                theme.Space16, theme.Space12, 154, 66);
            editButton.interactable = !hasActiveSession;
            startButton = factory.TouchButton(surface.transform,
                hasActiveSession ? "讲解进行中" : "开始讲解", !hasActiveSession,
                () => StartRequested?.Invoke(route));
            TouchUiFactory.Anchor(startButton.GetComponent<RectTransform>(), 0, 0, 1, 0,
                166, theme.Space12, -theme.Space16, 66);
            startButton.interactable = canStart && !hasActiveSession;
            if (!startButton.interactable) startButton.GetComponent<Image>().color = theme.SecondaryButton;

            if (!string.IsNullOrWhiteSpace(coverUrl))
                imageLoader.Load(cover, coverUrl, success =>
                {
                    if (placeholder != null) placeholder.gameObject.SetActive(!success);
                    if (!success && cover != null) cover.color = theme.PrimarySoft;
                });
        }

        public void RefreshTheme()
        {
            frame.color = theme.Border;
            accentBar.color = theme.ConfigurableAccent;
            if (placeholder != null)
            {
                placeholder.color = theme.ConfigurableAccent;
                if (cover.sprite == null) cover.color = theme.PrimarySoft;
            }
        }
    }
}
