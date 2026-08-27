using System;
using TG.Control.Touch.UI.Theme;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace TG.Control.Touch.UI.Components
{
    /// <summary>Creates the existing runtime UGUI primitives without owning any business behavior.</summary>
    public sealed class TouchUiFactory
    {
        private readonly Font font;
        private readonly Func<Color> accent;
        private readonly TouchTheme theme;

        public TouchUiFactory(Font font, Func<Color> accent, TouchTheme theme)
        {
            this.font = font ?? throw new ArgumentNullException(nameof(font));
            this.accent = accent ?? throw new ArgumentNullException(nameof(accent));
            this.theme = theme ?? throw new ArgumentNullException(nameof(theme));
        }

        public RectTransform ScrollGrid(Transform parent, string prefix, int columns, Vector2 cellSize, Vector2 spacing)
        {
            var viewport = Image(prefix + " Viewport", parent, new Color(1, 1, 1, 0));
            viewport.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1;
            viewport.gameObject.AddComponent<RectMask2D>();
            var root = Rect(prefix + " Grid", viewport.transform);
            Anchor(root, 0, 1, 1, 1, 0, 0, 0, 0);
            root.pivot = new Vector2(.5f, 1);
            var grid = root.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = cellSize;
            grid.spacing = spacing;
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = columns;
            grid.childAlignment = TextAnchor.UpperLeft;
            root.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport.rectTransform;
            scroll.content = root;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.scrollSensitivity = 36;
            return root;
        }

        public RectTransform Row(Transform parent, float spacing, float height)
        {
            var row = Rect("Row", parent);
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = height;
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = spacing;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            return row;
        }

        public Button Button(Transform parent, string text, bool primary, UnityAction action)
        {
            var image = Image(text, parent, primary ? accent() : theme.SecondaryButton);
            if (primary) image.gameObject.name = "Primary - " + text;
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(action);
            var colors = button.colors;
            colors.highlightedColor = primary ? theme.PrimaryHighlight : theme.SecondaryHighlight;
            colors.pressedColor = primary ? theme.PrimaryPressed : theme.SecondaryPressed;
            colors.disabledColor = theme.Disabled;
            button.colors = colors;
            var label = Label("Label", image.transform, text, theme.ButtonText, FontStyle.Bold,
                theme.TextPrimary, TextAnchor.MiddleCenter);
            Stretch(label.rectTransform, 6, 3, -6, -3);
            return button;
        }

        public InputField Input(Transform parent, string placeholder, int size, float height)
        {
            var image = Image("Route Name Input", parent, theme.InputBackground);
            image.gameObject.AddComponent<LayoutElement>().preferredHeight = height;
            var field = image.gameObject.AddComponent<InputField>();
            field.targetGraphic = image;
            field.lineType = InputField.LineType.SingleLine;
            field.characterLimit = 20;
            var text = Label("Text", image.transform, string.Empty, size, FontStyle.Normal, theme.Ink, TextAnchor.MiddleLeft);
            Stretch(text.rectTransform, 16, 5, -16, -5);
            text.supportRichText = false;
            var hintColor = theme.TextSecondary;
            hintColor.a = .65f;
            var hint = Label("Placeholder", image.transform, placeholder, size, FontStyle.Normal, hintColor, TextAnchor.MiddleLeft);
            Stretch(hint.rectTransform, 16, 5, -16, -5);
            field.textComponent = text;
            field.placeholder = hint;
            return field;
        }

        public RectTransform Panel(string name, Transform parent, Color color) => Image(name, parent, color).rectTransform;

        public Image Image(string name, Transform parent, Color color)
        {
            var image = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            image.transform.SetParent(parent, false);
            image.color = color;
            return image;
        }

        public RectTransform Rect(string name, Transform parent)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
        }

        public Text Label(string name, Transform parent, string value, int size, FontStyle style, Color color, TextAnchor alignment)
        {
            var label = new GameObject(name, typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            label.transform.SetParent(parent, false);
            label.font = font;
            label.fontSize = size;
            label.fontStyle = style;
            label.color = color;
            label.alignment = alignment;
            label.text = value;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            label.raycastTarget = false;
            return label;
        }

        public Text LayoutLabel(Transform parent, string value, int size, FontStyle style, Color color, float height)
        {
            var label = Label(value, parent, value, size, style, color, TextAnchor.MiddleLeft);
            label.gameObject.AddComponent<LayoutElement>().preferredHeight = height;
            return label;
        }

        public void FreeLabel(Transform parent, string value, int size, FontStyle style, Color color, float left, float top, float right, float height, TextAnchor alignment = TextAnchor.MiddleLeft)
        {
            var label = Label(value, parent, value, size, style, color, alignment);
            Anchor(label.rectTransform, 0, 1, 1, 1, left, -top - height, -right, -top);
        }

        public void Divider(Transform parent)
        {
            var divider = Image("Divider", parent, theme.Divider);
            divider.gameObject.AddComponent<LayoutElement>().preferredHeight = 2;
        }

        public static void Clear(Transform root)
        {
            for (var i = root.childCount - 1; i >= 0; i--)
            {
                root.GetChild(i).gameObject.SetActive(false);
                UnityEngine.Object.Destroy(root.GetChild(i).gameObject);
            }
        }

        public static void Stretch(RectTransform rect, float left = 0, float bottom = 0, float right = 0, float top = 0) =>
            Anchor(rect, 0, 0, 1, 1, left, bottom, right, top);

        public static void Anchor(RectTransform rect, float minX, float minY, float maxX, float maxY, float left, float bottom, float right, float top)
        {
            rect.anchorMin = new Vector2(minX, minY);
            rect.anchorMax = new Vector2(maxX, maxY);
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(right, top);
        }
    }
}
