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
            root = factory.Image("Side Navigation", parent, Color.clear);
            root.raycastTarget = false;
            var signal = factory.Image("Navigation Signal", root.transform,
                theme.ShellHighlight);
            TouchUiFactory.Anchor(signal.rectTransform, 0, 1, 0, 1, 28, -34, 64, -31);
            signal.raycastTarget = false;

            var heading = factory.Label("Navigation Heading", root.transform, "展厅讲解", 23,
                FontStyle.Bold, theme.TextPrimary, TextAnchor.MiddleLeft);
            TouchUiFactory.Anchor(heading.rectTransform, 0, 1, 1, 1,
                28, -74, -theme.PagePadding, -38);
            var caption = factory.Label("Navigation Caption", root.transform, "讲解导航", 14,
                FontStyle.Normal, theme.ShellHighlight, TextAnchor.MiddleLeft);
            TouchUiFactory.Anchor(caption.rectTransform, 0, 1, 1, 1,
                28, -98, -theme.PagePadding, -76);

            CreateItem(factory, TouchShellSection.ReceptionHome, "讲解首页", 0);
            CreateItem(factory, TouchShellSection.Playback, "当前讲解", 1);
            CreateItem(factory, TouchShellSection.SystemStatus, "系统状态", 2);
            CreateItem(factory, TouchShellSection.Routes, "讲解路线", 3);
            CreateItem(factory, TouchShellSection.Combination, "主题组合", 4);

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
            foreach (var item in items) Apply(item);
        }

        private void CreateItem(TouchUiFactory factory, TouchShellSection section, string text, int index)
        {
            var image = factory.RoundedImage("Navigation - " + text, root.transform,
                Color.clear);
            var top = -(120 + index * (theme.NavigationItemHeight + theme.Space8));
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
            var textLabel = factory.Label("Label", image.transform, text, 22, FontStyle.Bold,
                theme.TextPrimary, TextAnchor.MiddleLeft);
            TouchUiFactory.Anchor(textLabel.rectTransform, 0, 0, 1, 1, theme.Space32, 0, -theme.Space32, 0);
            var activity = factory.RoundedImage("Activity", image.transform, theme.Success);
            TouchUiFactory.Anchor(activity.rectTransform, 1, .5f, 1, .5f,
                -theme.Space24, -5, -theme.Space16, 5);
            activity.gameObject.SetActive(false);
            items.Add(new Item
            {
                Section = section,
                Background = image,
                Accent = accent,
                Label = textLabel,
                Activity = activity,
                Button = button
            });
        }

        private void Apply(Item item)
        {
            var selected = item.Section == active;
            item.Background.color = selected
                ? new Color(.02f, .26f, .65f, .84f)
                : Color.clear;
            item.Accent.gameObject.SetActive(selected);
            item.Accent.color = theme.ShellHighlight;
            item.Label.color = !item.Available ? theme.Disabled
                : selected ? theme.TextPrimary : theme.TextSecondary;
            item.Activity.gameObject.SetActive(item.Section == TouchShellSection.Playback && item.Available);
            item.Activity.color = theme.Success;
        }
    }
}
