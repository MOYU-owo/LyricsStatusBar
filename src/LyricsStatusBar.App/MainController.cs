using System.Drawing;
using System.Windows.Threading;
using LyricsStatusBar.Core;
using Forms = System.Windows.Forms;

namespace LyricsStatusBar.App;

internal sealed class MainController : IDisposable
{
    private const string ProductName = "\u7f51\u6613\u4e91\u4efb\u52a1\u680f\u6b4c\u8bcd";
    private const int PlacementFailureHideThreshold = 3;
    private const int HideRecordLimit = 10;
    private readonly SettingsStore _settingsStore = new();
    private readonly TaskbarLocator _taskbarLocator = new();
    private readonly BridgePipeServer _bridge = new();
    private readonly BridgeFileServer _fileBridge = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly Forms.NotifyIcon _trayIcon = new();
    private readonly Forms.ToolStripMenuItem _enabledItem = new("\u542f\u7528\u6b4c\u8bcd");
    private readonly Forms.ToolStripMenuItem _autoStartItem = new("\u5f00\u673a\u542f\u52a8");
    private readonly Queue<string> _hideRecords = new();

    private Icon? _applicationIcon;

    private OverlayWindow? _overlay;
    private AppSettings _settings = new();
    private TrackData? _track;
    private LyricTimeline? _timeline;
    private DisplayLine _lastLine = new(string.Empty, string.Empty);
    private DateTimeOffset _lastProgressAt = DateTimeOffset.MinValue;
    private TaskbarSnapshot? _lastTaskbar;
    private int _placementFailureCount;
    private string _pluginVersion = "\u672a\u8fde\u63a5";
    private string _clientVersion = "\u672a\u77e5";
    private string _pluginDeploymentStatus = "尚未检查";
    private DateTimeOffset _lastPluginDeploymentCheck = DateTimeOffset.MinValue;
    private bool _syncingMenus;
    private bool _disposed;

    public void Start()
    {
        _settings = _settingsStore.Load();
        _overlay = new OverlayWindow();
        _overlay.ApplySettings(_settings);
        _overlay.Show();
        ConfigureTray();
        CheckPluginDeployment(showResult: false);
        _bridge.MessageReceived += BridgeMessageReceived;
        _bridge.StatusChanged += BridgeStatusChanged;
        _bridge.Start();
        _fileBridge.MessageReceived += BridgeMessageReceived;
        _fileBridge.StatusChanged += BridgeStatusChanged;
        _fileBridge.Start();
        _timer.Tick += TimerTick;
        _timer.Start();
        TimerTick(this, EventArgs.Empty);
    }

    private void ConfigureTray()
    {
        _enabledItem.Checked = _settings.Enabled;
        _enabledItem.CheckOnClick = true;
        _enabledItem.CheckedChanged += (_, _) =>
        {
            if (_syncingMenus)
            {
                return;
            }
            _settings = _settings with { Enabled = _enabledItem.Checked };
            SaveSettings();
            if (!_settings.Enabled)
            {
                HideLyrics("disabled_by_menu");
            }
        };

        _autoStartItem.Checked = _settings.AutoStart;
        _autoStartItem.CheckOnClick = true;
        _autoStartItem.CheckedChanged += (_, _) =>
        {
            if (_syncingMenus)
            {
                return;
            }
            var requested = _autoStartItem.Checked;
            try
            {
                StartupRegistration.SetEnabled(requested);
                _settings = _settings with { AutoStart = requested };
                SaveSettings();
            }
            catch (Exception exception)
            {
                SetMenuChecks(_settings);
                Forms.MessageBox.Show($"Unable to change startup registration: {exception.Message}", ProductName);
            }
        };

        var settingsItem = new Forms.ToolStripMenuItem("\u8bbe\u7f6e\u2026");
        settingsItem.Click += (_, _) => OpenSettings();
        var diagnosticsItem = new Forms.ToolStripMenuItem("\u8bca\u65ad\u72b6\u6001");
        diagnosticsItem.Click += (_, _) => ShowDiagnostics();
        var repairPluginItem = new Forms.ToolStripMenuItem("安装/修复 BetterNCM 桥接");
        repairPluginItem.Click += (_, _) => CheckPluginDeployment(showResult: true);
        var exitItem = new Forms.ToolStripMenuItem("\u9000\u51fa");
        exitItem.Click += (_, _) => System.Windows.Application.Current.Shutdown();

        var menu = new Forms.ContextMenuStrip();
        menu.Items.AddRange(
        [
            _enabledItem,
            _autoStartItem,
            new Forms.ToolStripSeparator(),
            settingsItem,
            diagnosticsItem,
            repairPluginItem,
            new Forms.ToolStripSeparator(),
            exitItem
        ]);
        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath))
        {
            _applicationIcon = Icon.ExtractAssociatedIcon(processPath);
        }
        _trayIcon.Icon = _applicationIcon ?? SystemIcons.Application;
        _trayIcon.Text = ProductName;
        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.Visible = true;
        _trayIcon.DoubleClick += (_, _) => OpenSettings();
    }

    private void BridgeMessageReceived(BridgeMessage message)
    {
        System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
        {
            switch (message)
            {
                case HelloMessage hello:
                    _pluginVersion = hello.PluginVersion;
                    _clientVersion = hello.ClientVersion;
                    break;
                case TrackMessage track:
                    var isSameTrack = _track?.Id == track.Track.Id;
                    _track = track.Track;
                    _timeline = new LyricTimeline(track.Track.Original, track.Track.Translation);
                    if (!isSameTrack)
                    {
                        _lastLine = new DisplayLine(string.Empty, string.Empty);
                    }
                    if (track.Track.Original.Count == 0)
                    {
                        HideLyrics("track_has_no_original_lyrics");
                    }
                    break;
                case ProgressMessage progress when progress.TrackId == _track?.Id:
                    _lastProgressAt = DateTimeOffset.UtcNow;
                    var adjustedPositionMs = Math.Max(0, progress.PositionMs + _settings.LyricAdvanceMs);
                    var line = _timeline?.At(adjustedPositionMs) ?? new DisplayLine(string.Empty, string.Empty);
                    if (!line.IsEmpty && line != _lastLine && _settings.Enabled)
                    {
                        _lastLine = line;
                        _overlay?.SetLine(line);
                    }
                    break;
                case ClearMessage:
                    _track = null;
                    _timeline = null;
                    _lastLine = new DisplayLine(string.Empty, string.Empty);
                    HideLyrics("clear_message");
                    break;
            }
        });
    }

    private void BridgeStatusChanged()
    {
        if (!_bridge.IsConnected && !_fileBridge.IsConnected)
        {
            System.Windows.Application.Current.Dispatcher.BeginInvoke(() => HideLyrics("bridge_disconnected"));
        }
    }

    private void TimerTick(object? sender, EventArgs e)
    {
        if (DateTimeOffset.UtcNow - _lastPluginDeploymentCheck >= TimeSpan.FromSeconds(30))
        {
            CheckPluginDeployment(showResult: false);
        }

        var taskbar = _taskbarLocator.Locate(_settings);
        if (taskbar is null)
        {
            _placementFailureCount++;
            if (_lastTaskbar is null || _placementFailureCount >= PlacementFailureHideThreshold)
            {
                HideLyrics("taskbar_locate_failed");
            }
            return;
        }

        _placementFailureCount = 0;
        _lastTaskbar = taskbar;
        _overlay?.SetPlacement(taskbar.Placement);
        var stale = DateTimeOffset.UtcNow - _lastProgressAt > TimeSpan.FromMilliseconds(_settings.HideDelayMs);
        if (!_settings.Enabled || (!_bridge.IsConnected && !_fileBridge.IsConnected) || stale || (_track is null && _lastLine.IsEmpty))
        {
            HideLyrics($"timer_state enabled={_settings.Enabled} bridge={_bridge.IsConnected || _fileBridge.IsConnected} stale={stale} empty={_lastLine.IsEmpty} track={_track is not null}");
        }
    }

    private void CheckPluginDeployment(bool showResult)
    {
        _lastPluginDeploymentCheck = DateTimeOffset.UtcNow;
        var previousStatus = _pluginDeploymentStatus;
        var result = BetterNcmPluginDeployment.TryInstall();
        _pluginDeploymentStatus = result.Status;
        if (showResult)
        {
            Forms.MessageBox.Show(result.Status, ProductName, Forms.MessageBoxButtons.OK, Forms.MessageBoxIcon.Information);
        }
        else if (result.Changed && previousStatus != result.Status)
        {
            _trayIcon.BalloonTipTitle = ProductName;
            _trayIcon.BalloonTipText = result.Status;
            _trayIcon.ShowBalloonTip(5000);
        }
    }

    private void HideLyrics(string reason)
    {
        var staleMs = _lastProgressAt == DateTimeOffset.MinValue ? -1 : (int)Math.Max(0, (DateTimeOffset.UtcNow - _lastProgressAt).TotalMilliseconds);
        var record = $"{DateTime.Now:HH:mm:ss.fff} {reason}; pipe={_bridge.IsConnected}; file={_fileBridge.IsConnected}; staleMs={staleMs}; placementFail={_placementFailureCount}; lastLine={(_lastLine.IsEmpty ? "<empty>" : _lastLine.Primary)}";
        _hideRecords.Enqueue(record);
        while (_hideRecords.Count > HideRecordLimit)
        {
            _hideRecords.Dequeue();
        }
        _overlay?.HideLyrics();
    }

    private void OpenSettings()
    {
        var window = new SettingsWindow(_settings);
        if (window.ShowDialog() != true || window.ResultSettings is null)
        {
            return;
        }

        var previous = _settings;
        _settings = window.ResultSettings;
        if (previous.AutoStart != _settings.AutoStart)
        {
            try
            {
                StartupRegistration.SetEnabled(_settings.AutoStart);
            }
            catch (Exception exception)
            {
                Forms.MessageBox.Show($"Unable to change startup registration: {exception.Message}", ProductName);
                _settings = _settings with { AutoStart = previous.AutoStart };
            }
        }
        SetMenuChecks(_settings);
        _overlay?.ApplySettings(_settings);
        SaveSettings();
    }

    private void SetMenuChecks(AppSettings settings)
    {
        _syncingMenus = true;
        _enabledItem.Checked = settings.Enabled;
        _autoStartItem.Checked = settings.AutoStart;
        _syncingMenus = false;
    }

    private void SaveSettings()
    {
        try
        {
            _settingsStore.Save(_settings);
        }
        catch (Exception exception)
        {
            Forms.MessageBox.Show($"Unable to save settings: {exception.Message}", ProductName);
        }
    }

    private void ShowDiagnostics()
    {
        var track = _track is null ? "None" : $"{_track.Title} - {_track.Artist} ({_track.Id})";
        var taskbar = _lastTaskbar?.Description ?? "No usable area or fullscreen application";
        var lastMessageAt = new[] { _bridge.LastMessageAt, _fileBridge.LastMessageAt }
            .Where(value => value is not null)
            .Max();
        var lastMessage = lastMessageAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "None";
        var bridgeConnected = _bridge.IsConnected || _fileBridge.IsConnected;
        var lastError = string.Join("; ", new[] { _bridge.LastError, _fileBridge.LastError }.Where(value => value.Length > 0));
        var hideRecords = _hideRecords.Count == 0 ? "None" : string.Join("\n", _hideRecords.Reverse());
        var text =
            $"Bridge: {(bridgeConnected ? "Connected" : "Disconnected")}\n" +
            $"Pipe: {(_bridge.IsConnected ? "Connected" : "Disconnected")}\n" +
            $"File: {(_fileBridge.IsConnected ? "Connected" : "Disconnected")} {(_fileBridge.ActivePath ?? string.Empty)}\n" +
            $"Plugin: {_pluginVersion}\nClient: {_clientVersion}\n" +
            $"Plugin deployment: {_pluginDeploymentStatus}\n" +
            $"Track: {track}\nLast message: {lastMessage}\n" +
            $"Lyric advance: {_settings.LyricAdvanceMs} ms\n" +
            $"Taskbar: {taskbar}\nSettings: {_settingsStore.FilePath}\n" +
            $"Last error: {(lastError.Length == 0 ? "None" : lastError)}\n" +
            $"Hide records:\n{hideRecords}";
        Forms.MessageBox.Show(text, ProductName, Forms.MessageBoxButtons.OK, Forms.MessageBoxIcon.Information);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _timer.Stop();
        _bridge.Dispose();
        _fileBridge.Dispose();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _applicationIcon?.Dispose();
        _overlay?.Close();
    }
}
