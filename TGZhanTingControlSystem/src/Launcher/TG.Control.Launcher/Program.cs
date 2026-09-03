using System.Threading;

namespace TG.Control.Launcher;

internal static class Program
{
    private const string MutexName = "Global\\TG.Exhibition.RuntimeLauncher";

    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(true, MutexName, out var ownsMutex);
        if (!ownsMutex)
        {
            MessageBox.Show("TG Runtime Launcher 已经在运行。", "TG 智慧展厅",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        try
        {
            var configuration = LauncherConfiguration.Load();
            using var logger = new LauncherLog(configuration.LogDirectory);
            Application.Run(new RuntimeLauncherForm(configuration, logger));
        }
        catch (Exception exception)
        {
            MessageBox.Show("Launcher 启动失败：" + exception.Message, "TG 智慧展厅",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
