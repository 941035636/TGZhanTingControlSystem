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
            public Image Activity;
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

            var heading = factory.Label("Navigation Heading", root.transform, "接待工作台", theme.SectionTitle,
                FontStyle.Bold, theme.TextPrimary, TextAnchor.MiddleLeft);
            TouchUiFactory.Anchor(heading.rectTransform, 0, 1, 1, 1,
                theme.PagePadding, -64, -theme.PagePadding, -theme.Space16);
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
            var image = factory.RoundedImage("Navigation - " + text, root.transform, theme.NavigationBackground);
            var top = -(112 + index * (theme.NavigationItemHeight + theme.Space12));
            TouchUiFactory.Anchor(image.rectTransform, 0, 1, 1, 1,
                theme.Space12, top - theme.NavigationItemHeight, -theme.Space12, top);
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() =>
            {
                if (items.Find(value => value.Section == section)?.Available == true)
                    NavigateRequested?.Invoke(section);
            });
            var colors = button.colors;
            colors.normalColor = theme.NeutralTint;
            colors.highlightedColor = Color.Lerp(theme.NeutralTint, theme.PrimaryHover, .10f);
            colors.pressedColor = Color.Lerp(theme.NeutralTint, theme.PrimaryPressed, .18f);
            colors.disabledColor = theme.DisabledControlTint;
            colors.fadeDuration = .08f;
            button.colors = colors;

            var accent = factory.Image("Selection", image.transform, theme.Primary);
            TouchUiFactory.Anchor(accent.rectTransform, 0, 0, 0, 1, 0, theme.Space8, 5, -theme.Space8);
            var numberLabel = factory.Label("Index", image.transform, number, theme.Caption, FontStyle.Bold,
                theme.TextSecondary, TextAnchor.MiddleLeft);
            TouchUiFactory.Anchor(numberLabel.rectTransform, 0, 0, 0, 1,
                theme.Space16, 0, theme.Space16 + 34, 0);
            var textLabel = factory.Label("Label", image.transform, text, theme.Body, FontStyle.Bold,
                theme.TextPrimary, TextAnchor.MiddleLeft);
            TouchUiFactory.Anchor(textLabel.rectTransform, 0, 0, 1, 1, 58, 0, -theme.Space32, 0);
            var activity = factory.RoundedImage("Activity", image.transform, theme.Success);
            TouchUiFactory.Anchor(activity.rectTransform, 1, .5f, 1, .5f,
                -theme.Space24, -5, -theme.Space16, 5);
            activity.gameObject.SetActive(false);
            items.Add(new Item
            {
                Section = section,
                Background = image,
                Accent = accent,
                Number = numberLabel,
                Label = textLabel,
                Activity = activity,
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
            item.Activity.gameObject.SetActive(item.Section == TouchShellSection.Playback && item.Available);
            item.Activity.color = theme.Success;
        }
    }
}
