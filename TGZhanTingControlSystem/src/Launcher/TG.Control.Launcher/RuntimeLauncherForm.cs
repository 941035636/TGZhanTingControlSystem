using System.Diagnostics;

namespace TG.Control.Launcher;

internal sealed class RuntimeLauncherForm : Form
{
    private readonly LauncherConfiguration configuration;
    private readonly RuntimeSupervisor supervisor;
    private readonly CancellationTokenSource lifetime = new();
    private readonly Label serverStatus = StatusLabel("系统服务：等待中");
    private readonly Label touchStatus = StatusLabel("触控终端：未运行");
    private readonly Label ledStatus = StatusLabel("LED播放端：未运行");
    private readonly Label eventStatus = StatusLabel("正在初始化现场运行环境……");

    public RuntimeLauncherForm(LauncherConfiguration configuration, LauncherLog log)
    {
        this.configuration = configuration;
        supervisor = new RuntimeSupervisor(configuration, log);
        supervisor.StatusChanged += ApplySnapshot;
        log.Written += ApplyLog;

        Text = "TG 智慧展厅运行管理";
        Width = 760;
        Height = 480;
        MinimumSize = new Size(680, 420);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(10, 28, 51);
        ForeColor = Color.White;
        Font = new Font("Microsoft YaHei UI", 11f);

        var title = new Label
        {
            Text = "TG 智慧展厅运行管理",
            Font = new Font("Microsoft YaHei UI", 22f, FontStyle.Bold),
            ForeColor = Color.White,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 20)
        };
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(32),
            AutoScroll = true
        };
        panel.Controls.Add(title);
        panel.Controls.Add(serverStatus);
        panel.Controls.Add(touchStatus);
        panel.Controls.Add(ledStatus);
        panel.Controls.Add(eventStatus);

        var actions = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 24, 0, 0) };
        actions.Controls.Add(ActionButton("打开管理端", (_, _) => OpenAdmin()));
        actions.Controls.Add(ActionButton("启动触控端", (_, _) => supervisor.StartTouchNow()));
        actions.Controls.Add(ActionButton("启动LED端", (_, _) => supervisor.StartLedNow()));
        actions.Controls.Add(ActionButton("停止触控端", (_, _) => supervisor.StopTouch()));
        actions.Controls.Add(ActionButton("停止LED端", (_, _) => supervisor.StopLed()));
        panel.Controls.Add(actions);
        Controls.Add(panel);

        Shown += (_, _) => _ = supervisor.RunAsync(lifetime.Token);
        FormClosing += (_, _) => lifetime.Cancel();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            lifetime.Cancel();
            lifetime.Dispose();
            supervisor.Dispose();
        }
        base.Dispose(disposing);
    }

    private void ApplySnapshot(RuntimeSnapshot snapshot)
    {
        if (InvokeRequired) { BeginInvoke(() => ApplySnapshot(snapshot)); return; }
        serverStatus.Text = "系统服务：" + (snapshot.ServerOnline ? "在线" : "离线/启动中");
        serverStatus.ForeColor = snapshot.ServerOnline ? Color.FromArgb(89, 214, 166) : Color.FromArgb(255, 190, 92);
        touchStatus.Text = "触控终端：" + (snapshot.TouchClientRunning ? "运行中" : "未运行");
        ledStatus.Text = "LED播放端：" + (snapshot.LedPlayerRunning ? "运行中" : "未运行");
        eventStatus.Text = snapshot.Message;
    }

    private void ApplyLog(string line)
    {
        if (InvokeRequired) { BeginInvoke(() => ApplyLog(line)); return; }
        eventStatus.Text = line.Length > 110 ? line[^110..] : line;
    }

    private void OpenAdmin()
    {
        try { Process.Start(new ProcessStartInfo(configuration.AdminUrl) { UseShellExecute = true }); }
        catch (Exception exception) { eventStatus.Text = "无法打开管理端：" + exception.Message; }
    }

    private static Label StatusLabel(string text) => new()
    {
        Text = text,
        AutoSize = false,
        Width = 660,
        Height = 48,
        Padding = new Padding(16, 12, 16, 8),
        Margin = new Padding(0, 0, 0, 8),
        BackColor = Color.FromArgb(19, 45, 76),
        ForeColor = Color.FromArgb(215, 227, 240)
    };

    private static Button ActionButton(string text, EventHandler clicked)
    {
        var button = new Button
        {
            Text = text,
            Width = 122,
            Height = 46,
            Margin = new Padding(0, 0, 10, 10),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(25, 102, 177),
            ForeColor = Color.White
        };
        button.FlatAppearance.BorderSize = 0;
        button.Click += clicked;
        return button;
    }
}
