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
            public Image Icon;
            public Sprite LightIcon;
            public Sprite DarkNormalIcon;
            public Sprite DarkSelectedIcon;
            public Text Label;
            public Image Activity;
            public Button Button;
            public bool Available = true;
        }

        private readonly TouchTheme theme;
        private readonly Image root;
        private readonly Text heading;
        private readonly Text caption;
        private readonly List<Item> items = new List<Item>();
        private TouchShellSection active;
        private bool homeDarkMode;

        public RectTransform Root => root.rectTransform;
        public event Action<TouchShellSection> NavigateRequested;

        public SideNavigation(TouchUiFactory factory, TouchTheme theme, Transform parent)
        {
            this.theme = theme;
            root = factory.Image("Side Navigation", parent, Color.clear);
            root.raycastTarget = false;
            heading = factory.Label("Navigation Heading", root.transform, "展厅讲解", 23,
                FontStyle.Bold, theme.TextPrimary, TextAnchor.MiddleLeft);
            TouchUiFactory.Anchor(heading.rectTransform, 0, 1, 1, 1, 28, -54, -theme.PagePadding, -22);
            caption = factory.Label("Navigation Caption", root.transform, "讲解导航", 14,
                FontStyle.Normal, theme.ShellHighlight, TextAnchor.MiddleLeft);
            TouchUiFactory.Anchor(caption.rectTransform, 0, 1, 1, 1, 28, -79, -theme.PagePadding, -57);

            CreateItem(factory, TouchShellSection.ReceptionHome, "讲解首页", "home", 0);
            CreateItem(factory, TouchShellSection.Playback, "当前讲解", "microphone", 1);
            CreateItem(factory, TouchShellSection.SystemStatus, "系统状态", "activity", 2);
            CreateItem(factory, TouchShellSection.Routes, "讲解路线", "route", 3);
            CreateItem(factory, TouchShellSection.Combination, "主题组合", "layers", 4);

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
            root.color = Color.clear;
            heading.color = homeDarkMode ? theme.HomeDarkTextPrimary : theme.TextPrimary;
            caption.color = homeDarkMode ? theme.HomeDarkAccent : theme.ShellHighlight;
            foreach (var item in items) Apply(item);
        }

        public void SetHomeDarkMode(bool enabled)
        {
            if (homeDarkMode == enabled) return;
            homeDarkMode = enabled;
            RefreshTheme();
        }

        private void CreateItem(TouchUiFactory factory, TouchShellSection section, string text,
            string iconName, int index)
        {
            var image = factory.RoundedImage("Navigation - " + text, root.transform,
                Color.clear);
            var top = -(98 + index * (theme.NavigationItemHeight + theme.Space12));
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

            var accent = factory.Image("Selection", image.transform, theme.ShellHighlight);
            TouchUiFactory.Anchor(accent.rectTransform, 0, 0, 0, 1, 0, theme.Space12, 4, -theme.Space12);
            var icon = factory.Image("Navigation Icon - " + iconName, image.transform, Color.white);
            var darkIconName = iconName == "microphone" ? "mic"
                : iconName == "activity" ? "status"
                : iconName == "layers" ? "topic"
                : iconName;
            var texture = Resources.Load<Texture2D>("TGExhibitionUI/HomeDark/Icons/nav_" + darkIconName + "_128")
                ?? Resources.Load<Texture2D>("Touch/HomeFinal/nav_" + iconName + "_128");
            if (texture != null)
                icon.sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                    new Vector2(.5f, .5f));
            var lightIcon = icon.sprite;
            var darkNormalIcon = HomeDarkVisualAssets.NavigationIcon(darkIconName, false) ?? lightIcon;
            var darkSelectedIcon = HomeDarkVisualAssets.NavigationIcon(darkIconName, true) ?? darkNormalIcon;
            TouchUiFactory.Anchor(icon.rectTransform, 0, .5f, 0, .5f, 21, -15, 51, 15);
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            var textLabel = factory.Label("Label", image.transform, text, 22, FontStyle.Bold,
                theme.TextPrimary, TextAnchor.MiddleLeft);
            TouchUiFactory.Anchor(textLabel.rectTransform, 0, 0, 1, 1, 64, 0, -theme.Space16, 0);
            var activity = factory.RoundedImage("Activity", image.transform, theme.Success);
            TouchUiFactory.Anchor(activity.rectTransform, 1, .5f, 1, .5f,
                -theme.Space24, -5, -theme.Space16, 5);
            activity.gameObject.SetActive(false);
            items.Add(new Item
            {
                Section = section,
                Background = image,
                Accent = accent,
                Icon = icon,
                LightIcon = lightIcon,
                DarkNormalIcon = darkNormalIcon,
                DarkSelectedIcon = darkSelectedIcon,
                Label = textLabel,
                Activity = activity,
                Button = button
            });
        }

        private void Apply(Item item)
        {
            var selected = item.Section == active;
            var colors = item.Button.colors;
            if (homeDarkMode)
            {
                colors.normalColor = selected ? new Color(.025f, .32f, .72f, .88f) : Color.clear;
                colors.highlightedColor = selected
                    ? new Color(.035f, .40f, .88f, .94f)
                    : new Color(theme.HomeDarkAccent.r, theme.HomeDarkAccent.g, theme.HomeDarkAccent.b, .16f);
                colors.pressedColor = selected
                    ? new Color(.02f, .25f, .62f, 1f)
                    : new Color(theme.HomeDarkAccent.r, theme.HomeDarkAccent.g, theme.HomeDarkAccent.b, .28f);
                colors.disabledColor = new Color(.08f, .13f, .22f, .42f);
            }
            else
            {
                colors.normalColor = selected ? theme.ShellPanelActive : Color.clear;
                colors.highlightedColor = selected
                    ? Color.Lerp(theme.ShellPanelActive, theme.PrimaryHover, .10f)
                    : Color.Lerp(Color.clear, theme.PrimaryHover, .10f);
                colors.pressedColor = selected
                    ? Color.Lerp(theme.ShellPanelActive, theme.PrimaryPressed, .18f)
                    : Color.Lerp(Color.clear, theme.PrimaryPressed, .18f);
                colors.disabledColor = theme.DisabledControlTint;
            }
            colors.fadeDuration = .08f;
            item.Button.colors = colors;
            item.Background.color = colors.normalColor;
            item.Accent.gameObject.SetActive(selected);
            item.Accent.color = homeDarkMode ? theme.HomeDarkAccent : theme.ShellHighlight;
            item.Icon.sprite = homeDarkMode
                ? (selected ? item.DarkSelectedIcon : item.DarkNormalIcon)
                : item.LightIcon;
            item.Icon.color = !item.Available ? theme.DisabledControlTint
                : selected ? Color.white
                : homeDarkMode ? theme.HomeDarkTextSecondary : new Color(.57f, .67f, .79f, 1f);
            item.Label.color = !item.Available ? theme.Disabled
                : homeDarkMode ? (selected ? Color.white : theme.HomeDarkTextSecondary)
                : selected ? theme.TextPrimary : theme.TextSecondary;
            item.Activity.gameObject.SetActive(item.Section == TouchShellSection.Playback && item.Available);
            item.Activity.color = theme.Success;
        }
    }
}
