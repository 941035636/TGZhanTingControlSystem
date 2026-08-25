using TG.Control.UnityContracts;
using UnityEngine;

namespace TG.Control.LedPlayer
{
    public sealed class LedStatusOverlay : MonoBehaviour
    {
        [SerializeField] private LedApiClient apiClient;
        private bool connected;
        private bool hasCommand;
        private string status = "正在连接展厅控制服务";
        private GUIStyle logoStyle, titleStyle, bodyStyle, statusStyle;
        private Texture2D white;

        private void Start()
        {
            apiClient.ConnectionChanged += OnConnectionChanged;
            apiClient.CommandReceived += OnCommand;
        }

        private void OnDestroy()
        {
            if (apiClient == null) return;
            apiClient.ConnectionChanged -= OnConnectionChanged;
            apiClient.CommandReceived -= OnCommand;
        }

        private void OnConnectionChanged(bool value)
        {
            connected = value;
            if (!hasCommand) status = value ? "系统已就绪，等待触控终端启动讲解" : "服务连接中断，正在自动重连";
        }

        private void OnCommand(PlaybackCommand command)
        {
            hasCommand = true;
            status = command.action == PlaybackAction.PlayVideo ? "正在准备展厅宣传片" : "正在执行同步控制指令";
        }

        private void OnGUI()
        {
            EnsureStyles();
            var sx = Screen.width / 1920f; var sy = Screen.height / 1080f;
            GUI.matrix = Matrix4x4.Scale(new Vector3(sx, sy, 1));
            if (!hasCommand)
            {
                Fill(new Rect(0, 0, 1920, 1080), new Color32(10, 31, 27, 255));
                Fill(new Rect(0, 0, 1920, 1080), new Color(0.04f, 0.18f, 0.14f, .55f));
                GUI.Label(new Rect(790, 318, 340, 100), "TG", logoStyle);
                GUI.Label(new Rect(410, 460, 1100, 90), "展厅自动讲解系统", titleStyle);
                GUI.Label(new Rect(410, 570, 1100, 55), status, bodyStyle);
            }
            Fill(new Rect(48, 42, 360, 52), connected ? new Color32(28, 101, 77, 235) : new Color32(128, 76, 37, 235));
            GUI.Label(new Rect(68, 50, 330, 38), connected ? "●  LED 播放端在线" : "●  LED 播放端连接中", statusStyle);
        }

        private void EnsureStyles()
        {
            if (white != null) return;
            white = Texture2D.whiteTexture;
            logoStyle = new GUIStyle(GUI.skin.label) { fontSize = 70, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color32(210, 180, 111, 255) } };
            titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 58, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
            bodyStyle = new GUIStyle(GUI.skin.label) { fontSize = 25, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color32(185, 205, 197, 255) } };
            statusStyle = new GUIStyle(GUI.skin.label) { fontSize = 19, normal = { textColor = Color.white } };
        }

        private void Fill(Rect rect, Color color) { var previous = GUI.color; GUI.color = color; GUI.DrawTexture(rect, white); GUI.color = previous; }
    }
}
