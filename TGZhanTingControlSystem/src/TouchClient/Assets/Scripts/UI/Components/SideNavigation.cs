using System;
using System.Collections.Generic;
using TG.Control.Touch.UI.Theme;
using UnityEngine;
using UnityEngine.UI;

namespace TG.Control.Touch.UI.Components
{
    public enum TouchShellSection { ReceptionHome, Routes, Combination, Playback, SystemStatus }

    /// <summary>Navigation for the five capabilities that actually exist in TouchClient V1.</summary>
    public sealed class SideNavigation
    {
        private sealed class Item
        {
            public TouchShellSection Section;
            public Image Background;
            public Image Accent;
            public Text Number;
            public Text Label;
            public Button Button;
            public bool Available = true;
        }

        private readonly TouchTheme theme;
        private readonly Image root;
        private readonly List<Item> items = new List<Item>();
        private TouchShellSection active;

        public RectTransform Root => root.rectTransform;
        public event Action<TouchShellSection> NavigateRequested;

        public SideNavigation(TouchUiFactory factory, TouchTheme theme, Transform parent)
        {
            this.theme = theme;
            root = factory.Image("Side Navigation", parent, theme.NavigationBackground);
            var border = factory.Image("Navigation Border", root.transform, theme.Border);
            TouchUiFactory.Anchor(border.rectTransform, 1, 0, 1, 1, -1, 0, 0, 0);

            var heading = factory.Label("Navigation Heading", root.transform, "功能导航", theme.CardTitle,
                FontStyle.Bold, theme.TextPrimary, TextAnchor.MiddleLeft);
            TouchUiFactory.Anchor(heading.rectTransform, 0, 1, 1, 1,
                theme.PagePadding, -64, -theme.PagePadding, -theme.CardSpacing);
            var caption = factory.Label("Navigation Caption", root.transform, "EXHIBITION CONTROL", theme.Caption,
                FontStyle.Normal, theme.TextSecondary, TextAnchor.MiddleLeft);
            TouchUiFactory.Anchor(caption.rectTransform, 0, 1, 1, 1,
                theme.PagePadding, -88, -theme.PagePadding, -62);

            CreateItem(factory, TouchShellSection.ReceptionHome, "01", "接待首页", 0);
            CreateItem(factory, TouchShellSection.Routes, "02", "讲解路线", 1);
            CreateItem(factory, TouchShellSection.Combination, "03", "主题组合", 2);
            CreateItem(factory, TouchShellSection.Playback, "04", "当前讲解", 3);
            CreateItem(factory, TouchShellSection.SystemStatus, "05", "系统状态", 4);

            var footer = factory.Label("Navigation Footer", root.transform,
                "55英寸触控终端\n仅展示当前可用功能", theme.Caption, FontStyle.Normal,
                theme.TextSecondary, TextAnchor.LowerLeft);
            TouchUiFactory.Anchor(footer.rectTransform, 0, 0, 1, 0,
                theme.PagePadding, theme.PagePadding, -theme.PagePadding, theme.PagePadding + 58);
            SetActive(TouchShellSection.ReceptionHome);
            SetPlaybackAvailable(false);
        }

        public void SetActive(TouchShellSection section)
        {
            active = section;
            foreach (var item in items) Apply(item);
        }

        public void SetPlaybackAvailable(bool available)
        {
            var item = items.Find(value => value.Section == TouchShellSection.Playback);
            if (item == null) return;
            item.Available = available;
            item.Button.interactable = available;
            Apply(item);
        }

        public void RefreshTheme()
        {
            root.color = theme.NavigationBackground;
            foreach (var item in items) Apply(item);
        }

        private void CreateItem(TouchUiFactory factory, TouchShellSection section, string number, string text, int index)
        {
            var image = factory.Image("Navigation - " + text, root.transform, theme.NavigationBackground);
            var top = -(112 + index * (theme.ButtonHeight + 12));
            TouchUiFactory.Anchor(image.rectTransform, 0, 1, 1, 1,
                12, top - theme.ButtonHeight, -12, top);
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() =>
            {
                if (items.Find(value => value.Section == section)?.Available == true)
                    NavigateRequested?.Invoke(section);
            });
            var colors = button.colors;
            colors.highlightedColor = theme.SecondaryHighlight;
            colors.pressedColor = theme.PrimaryPressed;
            colors.disabledColor = theme.NeutralTint;
            button.colors = colors;

            var accent = factory.Image("Selection", image.transform, theme.Primary);
            TouchUiFactory.Anchor(accent.rectTransform, 0, 0, 0, 1, 0, 0, 4, 0);
            var numberLabel = factory.Label("Index", image.transform, number, theme.Caption, FontStyle.Bold,
                theme.TextSecondary, TextAnchor.MiddleLeft);
            TouchUiFactory.Anchor(numberLabel.rectTransform, 0, 0, 0, 1, theme.CardSpacing, 0, theme.CardSpacing + 34, 0);
            var textLabel = factory.Label("Label", image.transform, text, theme.Body, FontStyle.Bold,
                theme.TextPrimary, TextAnchor.MiddleLeft);
            TouchUiFactory.Anchor(textLabel.rectTransform, 0, 0, 1, 1, 58, 0, -theme.CardSpacing, 0);
            items.Add(new Item
            {
                Section = section,
                Background = image,
                Accent = accent,
                Number = numberLabel,
                Label = textLabel,
                Button = button
            });
        }

        private void Apply(Item item)
        {
            var selected = item.Section == active;
            item.Background.color = selected ? theme.PrimaryMuted : theme.NavigationBackground;
            item.Accent.gameObject.SetActive(selected);
            item.Accent.color = theme.Primary;
            item.Number.color = !item.Available ? theme.Disabled : selected ? theme.Primary : theme.TextSecondary;
            item.Label.color = !item.Available ? theme.Disabled : theme.TextPrimary;
        }
    }
}
