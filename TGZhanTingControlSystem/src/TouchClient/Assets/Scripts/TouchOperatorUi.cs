using System;
using System.Collections.Generic;
using System.Linq;
using TG.Control.UnityContracts;
using UnityEngine;

namespace TG.Control.Touch
{
    public sealed class TouchOperatorUi : MonoBehaviour
    {
        private const float ReferenceHeight = 1840f;
        private const float MinimumCanvasWidth = 2800f;
        [SerializeField] private TouchApiClient apiClient;
        [SerializeField] private TouchControlFacade facade;

        private readonly HashSet<string> selected = new HashSet<string>();
        private PublishedContent content;
        private bool connected;
        private string status = "正在连接展厅服务…";
        private GUIStyle titleStyle, subtitleStyle, moduleStyle, moduleSelectedStyle, numberStyle, bodyStyle, smallStyle, statusStyle, primaryStyle, secondaryStyle;
        private Texture2D white;

        private void Start()
        {
            apiClient.ConnectionChanged += OnConnectionChanged;
            facade.ContentLoaded += OnContentLoaded;
            facade.Status += value => status = value;
            facade.Error += value => status = "操作失败：" + value;
            if (facade.CurrentContent != null) OnContentLoaded(facade.CurrentContent);
        }

        private void OnDestroy()
        {
            if (apiClient != null) apiClient.ConnectionChanged -= OnConnectionChanged;
            if (facade != null)
            {
                facade.ContentLoaded -= OnContentLoaded;
            }
        }

        private void OnConnectionChanged(bool value)
        {
            connected = value;
            status = value ? "系统已连接，可开始讲解。" : "服务器连接中断，正在自动重连…";
        }

        private void OnContentLoaded(PublishedContent value)
        {
            content = value;
            selected.RemoveWhere(id => value.modules.All(module => module.id != id || !module.enabled));
            status = $"内容版本 V{value.version} 已加载，共 {value.modules.Length} 个主题。";
        }

        private void OnGUI()
        {
            EnsureStyles();
            // Preserve the original design height while allowing wide displays
            // to use their full horizontal area instead of adding side bars.
            var scale = Screen.height / ReferenceHeight;
            var canvasWidth = Mathf.Max(MinimumCanvasWidth, Screen.width / scale);
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));

            Fill(new Rect(0, 0, canvasWidth, ReferenceHeight), new Color32(239, 243, 240, 255));
            Fill(new Rect(0, 0, canvasWidth, 210), new Color32(18, 55, 45, 255));
            GUI.Label(new Rect(90, 46, 900, 72), "展厅自动讲解系统", titleStyle);
            GUI.Label(new Rect(92, 125, 1100, 40), "TG EXHIBITION · 智慧展陈中控终端", subtitleStyle);
            DrawStatusPill(new Rect(canvasWidth - 530, 70, 430, 72));

            DrawRoutePanel(new Rect(70, 270, 570, 1480));
            DrawModulePanel(new Rect(690, 270, canvasWidth - 760, 1480));
        }

        private void DrawRoutePanel(Rect rect)
        {
            Panel(rect);
            GUI.Label(new Rect(rect.x + 42, rect.y + 38, rect.width - 84, 50), "讲解路线", moduleStyle);
            GUI.Label(new Rect(rect.x + 42, rect.y + 98, rect.width - 84, 60), "按右侧主题选择任意组合，系统将按编号顺序依次讲解。", bodyStyle);
            Fill(new Rect(rect.x + 42, rect.y + 185, rect.width - 84, 2), new Color32(222, 228, 224, 255));

            var selectedModules = content?.modules.Where(item => selected.Contains(item.id)).OrderBy(item => item.order).ToArray() ?? Array.Empty<ExhibitionModule>();
            GUI.Label(new Rect(rect.x + 42, rect.y + 220, rect.width - 84, 38), $"已选择 {selectedModules.Length} 个主题", smallStyle);
            var y = rect.y + 280;
            foreach (var module in selectedModules.Take(12))
            {
                Fill(new Rect(rect.x + 42, y, rect.width - 84, 56), new Color32(244, 247, 245, 255));
                GUI.Label(new Rect(rect.x + 62, y + 9, 58, 36), module.order.ToString("00"), numberStyle);
                GUI.Label(new Rect(rect.x + 125, y + 7, rect.width - 185, 42), module.name, bodyStyle);
                y += 63;
            }
            if (selectedModules.Length == 0) GUI.Label(new Rect(rect.x + 42, y, rect.width - 84, 100), "尚未选择主题\n点击右侧卡片加入讲解路线", statusStyle);

            GUI.Label(new Rect(rect.x + 42, rect.yMax - 390, rect.width - 84, 55), status, statusStyle);
            if (GUI.Button(new Rect(rect.x + 42, rect.yMax - 315, rect.width - 84, 72), "开始组合讲解", primaryStyle))
            {
                facade.StartModules(selectedModules.Select(item => item.id).ToArray());
            }
            if (GUI.Button(new Rect(rect.x + 42, rect.yMax - 228, rect.width - 84, 62), "全部主题依次讲解", secondaryStyle)) facade.StartAll();
            var controlY = rect.yMax - 142;
            var controlWidth = (rect.width - 102) / 4f;
            if (GUI.Button(new Rect(rect.x + 42, controlY, controlWidth, 62), "暂停", secondaryStyle)) facade.Pause();
            if (GUI.Button(new Rect(rect.x + 48 + controlWidth, controlY, controlWidth, 62), "继续", secondaryStyle)) facade.Resume();
            if (GUI.Button(new Rect(rect.x + 54 + controlWidth * 2, controlY, controlWidth, 62), "跳过", secondaryStyle)) facade.Skip();
            if (GUI.Button(new Rect(rect.x + 60 + controlWidth * 3, controlY, controlWidth, 62), "终止", secondaryStyle)) facade.Stop();
        }

        private void DrawModulePanel(Rect rect)
        {
            GUI.Label(new Rect(rect.x, rect.y, 700, 58), "选择讲解主题", moduleStyle);
            GUI.Label(new Rect(rect.xMax - 580, rect.y + 12, 580, 38), content == null ? "内容载入中…" : $"正式内容 V{content.version}", smallStyle);
            var modules = content?.modules.Where(item => item.enabled).OrderBy(item => item.order).ToArray() ?? Array.Empty<ExhibitionModule>();
            const int columns = 3;
            const float gap = 28;
            var cardWidth = (rect.width - gap * (columns - 1)) / columns;
            var rows = Mathf.Max(1, Mathf.CeilToInt(modules.Length / (float)columns));
            var cardHeight = (rect.height - 90 - gap * (rows - 1)) / rows;
            for (var i = 0; i < modules.Length; i++)
            {
                var row = i / columns; var column = i % columns;
                var card = new Rect(rect.x + column * (cardWidth + gap), rect.y + 90 + row * (cardHeight + gap), cardWidth, cardHeight);
                var module = modules[i]; var isSelected = selected.Contains(module.id);
                if (GUI.Button(card, GUIContent.none, isSelected ? moduleSelectedStyle : moduleStyle))
                {
                    if (!selected.Add(module.id)) selected.Remove(module.id);
                }
                GUI.Label(new Rect(card.x + 30, card.y + 26, 90, 45), module.order.ToString("00"), numberStyle);
                GUI.Label(new Rect(card.x + 30, card.y + 92, card.width - 60, 60), module.name, moduleStyle);
                GUI.Label(new Rect(card.x + 30, card.y + 166, card.width - 60, Mathf.Max(50, card.height - 225)), string.IsNullOrWhiteSpace(module.description) ? "展厅主题讲解内容" : module.description, bodyStyle);
                GUI.Label(new Rect(card.x + 30, card.yMax - 45, card.width - 60, 30), isSelected ? "✓ 已加入路线" : "点击选择", smallStyle);
            }
        }

        private void DrawStatusPill(Rect rect)
        {
            Fill(rect, connected ? new Color32(38, 111, 87, 255) : new Color32(126, 85, 45, 255));
            GUI.Label(new Rect(rect.x + 25, rect.y + 17, rect.width - 50, 40), connected ? "●  服务在线" : "●  正在连接", subtitleStyle);
        }

        private void Panel(Rect rect) { Fill(rect, Color.white); }
        private void Fill(Rect rect, Color color) { var old = GUI.color; GUI.color = color; GUI.DrawTexture(rect, white); GUI.color = old; }

        private void EnsureStyles()
        {
            if (white != null) return;
            white = Texture2D.whiteTexture;
            titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 48, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
            subtitleStyle = new GUIStyle(GUI.skin.label) { fontSize = 23, normal = { textColor = new Color32(202, 218, 211, 255) } };
            moduleStyle = new GUIStyle(GUI.skin.button) { fontSize = 30, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(0, 0, 0, 0), normal = { textColor = new Color32(25, 55, 46, 255), background = MakeColor(new Color32(255, 255, 255, 255)) } };
            moduleSelectedStyle = new GUIStyle(moduleStyle) { normal = { background = MakeColor(new Color32(225, 238, 231, 255)) }, hover = { background = MakeColor(new Color32(218, 234, 225, 255)) } };
            numberStyle = new GUIStyle(GUI.skin.label) { fontSize = 25, fontStyle = FontStyle.Bold, normal = { textColor = new Color32(190, 151, 79, 255) } };
            bodyStyle = new GUIStyle(GUI.skin.label) { fontSize = 22, wordWrap = true, normal = { textColor = new Color32(86, 105, 98, 255) } };
            smallStyle = new GUIStyle(GUI.skin.label) { fontSize = 19, alignment = TextAnchor.MiddleRight, normal = { textColor = new Color32(112, 130, 123, 255) } };
            statusStyle = new GUIStyle(bodyStyle) { alignment = TextAnchor.MiddleCenter };
            primaryStyle = ButtonStyle(new Color32(28, 91, 70, 255), Color.white, 27);
            secondaryStyle = ButtonStyle(new Color32(235, 239, 237, 255), new Color32(28, 77, 61, 255), 24);
        }

        private GUIStyle ButtonStyle(Color32 background, Color text, int fontSize) => new GUIStyle(GUI.skin.button) { fontSize = fontSize, fontStyle = FontStyle.Bold, normal = { background = MakeColor(background), textColor = text }, hover = { background = MakeColor(background), textColor = text } };
        private static Texture2D MakeColor(Color color) { var texture = new Texture2D(1, 1); texture.SetPixel(0, 0, color); texture.Apply(); return texture; }
    }
}
