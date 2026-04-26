using Microsoft.Win32;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace TimeTracker2K;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly DailyLogStore _store = new();
    private readonly Icon _appIcon = AppIcon.Create();
    private readonly DashboardForm _dashboard;
    private readonly NotifyIcon _notifyIcon;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly ToolStripMenuItem _startWithWindowsItem;

    public TrayApplicationContext()
    {
        _dashboard = new DashboardForm(_store, _appIcon);
        StartupRegistration.MigrateLegacyValue();

        _startWithWindowsItem = new ToolStripMenuItem("Start with Windows")
        {
            Checked = StartupRegistration.IsEnabled()
        };
        _startWithWindowsItem.Click += (_, _) => ToggleStartup();

        var menu = new ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => ShowDashboard());
        menu.Items.Add(_startWithWindowsItem);
        menu.Items.Add("Open data folder", null, (_, _) => OpenDataFolder());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitApplication());

        _notifyIcon = new NotifyIcon
        {
            ContextMenuStrip = menu,
            Icon = _appIcon,
            Text = "Time Tracker 2K",
            Visible = false
        };
        _notifyIcon.MouseClick += OnNotifyIconMouseClick;
        _notifyIcon.DoubleClick += (_, _) => ShowDashboard();
        _notifyIcon.Visible = true;

        _timer = new System.Windows.Forms.Timer
        {
            Interval = 30_000
        };
        _timer.Tick += (_, _) => TimerTick();
        _timer.Start();

        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.SessionEnding += OnSessionEnding;
        SystemEvents.SessionSwitch += OnSessionSwitch;
        Application.ApplicationExit += OnApplicationExit;
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;

        TimerTick();
    }

    private void TimerTick()
    {
        _notifyIcon.Visible = true;
        _store.Checkpoint();
        RefreshTrayText();
    }

    private void RefreshTrayText()
    {
        var today = _store.GetTodaySummary();
        _notifyIcon.Text = $"Time Tracker 2K - {DurationFormatter.Format(today.LoggedIn)}";
    }

    private void ShowDashboard()
    {
        _dashboard.RefreshData();
        if (!_dashboard.Visible)
        {
            _dashboard.Show();
        }

        if (_dashboard.WindowState == FormWindowState.Minimized)
        {
            _dashboard.WindowState = FormWindowState.Normal;
        }

        NativeMethods.ShowWindow(_dashboard.Handle, NativeMethods.SwShownormal);
        _dashboard.BringToFront();
        _dashboard.Activate();
        NativeMethods.SetForegroundWindow(_dashboard.Handle);
    }

    private void OnNotifyIconMouseClick(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            ShowDashboard();
        }
    }

    private void ToggleStartup()
    {
        try
        {
            StartupRegistration.SetEnabled(!_startWithWindowsItem.Checked);
            _startWithWindowsItem.Checked = StartupRegistration.IsEnabled();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Could not update startup setting",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void OpenDataFolder()
    {
        Directory.CreateDirectory(_store.AppDataDirectory);
        Process.Start(new ProcessStartInfo
        {
            FileName = _store.AppDataDirectory,
            UseShellExecute = true
        });
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Suspend)
        {
            _store.Pause();
        }
        else if (e.Mode == PowerModes.Resume)
        {
            _store.Resume();
        }
    }

    private void OnSessionEnding(object? sender, SessionEndingEventArgs e)
    {
        if (e.Reason == SessionEndReasons.Logoff)
        {
            _store.MarkLoggedOutStarted(persistAcrossRestart: true);
            return;
        }

        _store.Pause();
    }

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is SessionSwitchReason.SessionLogoff
            or SessionSwitchReason.ConsoleDisconnect
            or SessionSwitchReason.RemoteDisconnect)
        {
            _store.MarkLoggedOutStarted(persistAcrossRestart: true);
        }
        else if (e.Reason == SessionSwitchReason.SessionLock)
        {
            _store.MarkLoggedOutStarted(persistAcrossRestart: false);
        }
        else if (e.Reason is SessionSwitchReason.SessionLogon
                 or SessionSwitchReason.SessionUnlock
                 or SessionSwitchReason.ConsoleConnect
                 or SessionSwitchReason.RemoteConnect)
        {
            _store.MarkLoggedInAgain();
        }
    }

    private void ExitApplication()
    {
        _store.Checkpoint();
        _notifyIcon.Visible = false;
        _timer.Stop();
        _dashboard.AllowApplicationClose();
        _dashboard.Close();
        ExitThread();
    }

    private void OnApplicationExit(object? sender, EventArgs e)
    {
        _store.Checkpoint();
    }

    private void OnProcessExit(object? sender, EventArgs e)
    {
        _store.Checkpoint();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            SystemEvents.SessionEnding -= OnSessionEnding;
            SystemEvents.SessionSwitch -= OnSessionSwitch;
            Application.ApplicationExit -= OnApplicationExit;
            AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
            _timer.Dispose();
            _notifyIcon.Dispose();
            _dashboard.Dispose();
            _appIcon.Dispose();
        }

        base.Dispose(disposing);
    }

    private static class NativeMethods
    {
        public const int SwShownormal = 1;

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetForegroundWindow(IntPtr hWnd);
    }
}
