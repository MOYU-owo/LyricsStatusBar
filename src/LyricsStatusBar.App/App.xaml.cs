using System.Threading;
using System.Windows;
using Forms = System.Windows.Forms;

namespace LyricsStatusBar.App;

public partial class App : System.Windows.Application
{
    private Mutex? _singleInstance;
    private MainController? _controller;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Any(argument => string.Equals(argument, "--repair-plugin", StringComparison.OrdinalIgnoreCase)))
        {
            var result = BetterNcmPluginDeployment.TryInstall();
            Forms.MessageBox.Show(
                result.Status,
                "\u7f51\u6613\u4e91\u4efb\u52a1\u680f\u6b4c\u8bcd",
                Forms.MessageBoxButtons.OK,
                Forms.MessageBoxIcon.Information);
            Shutdown();
            return;
        }

        _singleInstance = new Mutex(initiallyOwned: true, @"Local\LyricsStatusBar", out var createdNew);
        if (!createdNew)
        {
            _singleInstance.Dispose();
            _singleInstance = null;
            Shutdown();
            return;
        }
        _controller = new MainController();
        _controller.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _controller?.Dispose();
        if (_singleInstance is not null)
        {
            _singleInstance.ReleaseMutex();
            _singleInstance.Dispose();
        }
        base.OnExit(e);
    }
}