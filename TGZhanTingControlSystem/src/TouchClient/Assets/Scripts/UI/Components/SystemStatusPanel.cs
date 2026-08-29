using TG.Control.Touch.UI.Theme;
using TG.Control.UnityContracts;
using UnityEngine;
using UnityEngine.UI;

namespace TG.Control.Touch.UI.Components
{
    /// <summary>Reception summary using only states currently supplied by the server.</summary>
    public sealed class SystemStatusPanel
    {
        private readonly TouchTheme theme;
        private readonly Image frame;
        private readonly Image surface;
        private readonly Image stateIndicator;
        private readonly Text stateLabel;
        private readonly Text serverValue;
        private readonly Text ledValue;
        private readonly Text contentValue;
        private readonly Text readinessValue;

        public RectTransform Root => frame.rectTransform;

        public SystemStatusPanel(TouchUiFactory factory, TouchTheme theme, Transform parent)
        {
            this.theme = theme;
            frame = factory.RoundedImage("System Status Panel", parent, theme.Border);
            surface = factory.RoundedImage("System Status Surface", frame.transform, theme.SurfaceElevated);
            TouchUiFactory.Stretch(surface.rectTransform, 1, 1, -1, -1);

            var title = factory.Label("System Status Title", surface.transform, "系统接待状态", theme.CardTitle,
                FontStyle.Bold, theme.TextPrimary, TextAnchor.MiddleLeft);
            TouchUiFactory.Anchor(title.rectTransform, 0, 1, 1, 1,
                theme.PanelPadding, -50, -theme.PanelPadding, -theme.Space12);
            stateIndicator = factory.Image("System State Indicator", surface.transform, theme.TextSecondary);
            TouchUiFactory.Anchor(stateIndicator.rectTransform, 1, 1, 1, 1, -40, -38, -28, -26);

            stateLabel = factory.Label("System State", surface.transform, "状态检查中", theme.Body,
                FontStyle.Bold, theme.TextSecondary, TextAnchor.MiddleLeft);
            TouchUiFactory.Anchor(stateLabel.rectTransform, 0, 1, 1, 1,
                theme.PanelPadding, -90, -theme.PanelPadding, -54);
            var divider = factory.Image("System Divider", surface.transform, theme.Border);
            TouchUiFactory.Anchor(divider.rectTransform, 0, 1, 1, 1,
                theme.PanelPadding, -98, -theme.PanelPadding, -96);

            serverValue = CreateRow(factory, surface.transform, "Server连接", 106);
            ledValue = CreateRow(factory, surface.transform, "LED播放端", 142);
            contentValue = CreateRow(factory, surface.transform, "正式内容", 178);
            readinessValue = CreateRow(factory, surface.transform, "系统就绪", 214);
        }

        public void Render(TouchUiState state)
        {
            if (state == null) return;
            serverValue.text = state.Connected ? "在线" : "自动重连中";
            serverValue.color = state.Connected ? theme.Success : theme.Error;

            if (state.Readiness == null)
            {
                ledValue.text = "检查中";
                ledValue.color = theme.TextSecondary;
            }
            else if (!state.Readiness.ledOnline)
            {
                ledValue.text = "离线";
                ledValue.color = theme.Error;
            }
            else if (state.Readiness.ledReady)
            {
                ledValue.text = "在线 · 已就绪";
                ledValue.color = theme.Success;
            }
            else
            {
                ledValue.text = "在线 · 素材待同步";
                ledValue.color = theme.Warning;
            }

            var version = state.Content?.version ?? state.Readiness?.contentVersion ?? 0;
            contentValue.text = version > 0 ? "V" + version : "尚未加载";
            contentValue.color = version > 0 ? theme.TextPrimary : theme.TextSecondary;

            var presentation = ReceptionStatePresentation.From(state, theme);
            stateLabel.text = presentation.Title;
            stateLabel.color = presentation.Color;
            stateIndicator.color = presentation.Color;
            readinessValue.text = presentation.ShortLabel;
            readinessValue.color = presentation.Color;
        }

        public void RefreshTheme() => frame.color = theme.Border;

        private Text CreateRow(TouchUiFactory factory, Transform parent, string label, float top)
        {
            var key = factory.Label(label + " Label", parent, label, theme.Caption, FontStyle.Normal,
                theme.TextMuted, TextAnchor.MiddleLeft);
            TouchUiFactory.Anchor(key.rectTransform, 0, 1, .48f, 1,
                theme.PanelPadding, -top - 30, 0, -top);
            var value = factory.Label(label + " Value", parent, "—", theme.Caption, FontStyle.Bold,
                theme.TextPrimary, TextAnchor.MiddleRight);
            TouchUiFactory.Anchor(value.rectTransform, .42f, 1, 1, 1,
                0, -top - 30, -theme.PanelPadding, -top);
            return value;
        }
    }

    internal sealed class ReceptionStatePresentation
    {
        public string Title { get; private set; }
        public string ShortLabel { get; private set; }
        public string Detail { get; private set; }
        public Color Color { get; private set; }

        public static ReceptionStatePresentation From(TouchUiState state, TouchTheme theme)
        {
            if (state?.HasActiveSession == true)
                return Create("当前讲解进行中", "讲解进行中", "请继续当前讲解，结束后再发起新的接待。", theme.Primary);
            if (state?.Connected != true)
                return Create("Server连接异常", "连接异常", "系统正在自动重连，恢复连接后可继续操作。", theme.Error);
            if (state.Readiness == null)
                return Create("正在检查系统状态", "状态检查中", "正在读取LED播放端和正式内容状态。", theme.TextSecondary);
            if (!state.Readiness.ledOnline)
                return Create("LED播放端离线", "LED离线", "请启动或检查LED播放主机，在线后方可开始讲解。", theme.Error);
            if (state.Readiness.canStart && state.Readiness.ledReady)
                return Create("系统可接待", "已就绪", "服务、LED播放端和正式内容均已就绪。", theme.Success);
            if (state.Readiness.canStart)
                return Create("系统受限可用", "受限可用", "仍可开始讲解；缺失素材将按现有策略补齐或跳过。", theme.Warning);
            return Create("暂不可开始讲解", "暂不可接待",
                string.IsNullOrWhiteSpace(state.Readiness.message) ? "系统仍在准备，请稍候。" : state.Readiness.message,
                theme.Warning);
        }

        private static ReceptionStatePresentation Create(string title, string shortLabel, string detail, Color color) =>
            new ReceptionStatePresentation { Title = title, ShortLabel = shortLabel, Detail = detail, Color = color };
    }
}
