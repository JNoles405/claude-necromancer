using ClaudeNecromancer.Core;

namespace ClaudeNecromancer.UI;

/// <summary>
/// Owns the process lifetime. The window is incidental — closing it hides it, and the app carries on
/// touching from the tray. Only "Exit" actually ends the process.
/// </summary>
public sealed class TrayApp : ApplicationContext
{
    private readonly AppController _controller = new();
    private readonly NotifyIcon _tray;
    private readonly Icon _iconNormal;
    private readonly Icon _iconAlert;
    private MainForm? _window;
    private bool _exiting;

    private static readonly Color AccentNormal = Color.FromArgb(94, 200, 156);
    private static readonly Color AccentAlert = Color.FromArgb(232, 156, 76);

    public TrayApp(bool startMinimized)
    {
        _iconNormal = IconFactory.Create(AccentNormal);
        _iconAlert = IconFactory.Create(AccentAlert);

        _tray = new NotifyIcon
        {
            Icon = _iconNormal,
            Visible = true,
            Text = "Claude Necromancer",
            ContextMenuStrip = BuildMenu(),
        };
        _tray.DoubleClick += (_, _) => ShowWindow();

        _controller.SessionsChanged += OnSessionsChanged;
        _controller.RunCompleted += OnRunCompleted;

        // A timer can't fire while the machine is off, so catch up at launch.
        if (_controller.Config.TouchOnStartup)
        {
            var due = (_controller.Config.LastRunUtc ?? DateTime.MinValue) + _controller.Config.Interval;
            if (DateTime.UtcNow >= due)
            {
                Task.Run(() => _controller.RunTouch(manual: false));
            }
        }

        UpdateTrayState();

        if (!startMinimized && !_controller.Config.StartMinimized)
            ShowWindow();
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();

        var open = new ToolStripMenuItem("Open Claude Necromancer", null, (_, _) => ShowWindow())
        {
            Font = new Font(SystemFonts.MenuFont!, FontStyle.Bold),
        };
        menu.Items.Add(open);
        menu.Items.Add(new ToolStripSeparator());

        menu.Items.Add(new ToolStripMenuItem("Touch sessions now", null, (_, _) =>
            Task.Run(() => _controller.RunTouch(manual: true))));

        var pause = new ToolStripMenuItem("Pause schedule")
        {
            CheckOnClick = true,
            Checked = !_controller.Config.ScheduleEnabled,
        };
        pause.CheckedChanged += (_, _) =>
        {
            _controller.Config.ScheduleEnabled = !pause.Checked;
            _controller.Config.Save();
            UpdateTrayState();
            _window?.ReloadFromConfig();
        };
        menu.Items.Add(pause);

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Exit", null, (_, _) => ExitApp()));

        // Keep the checkbox honest if the schedule is toggled from the window instead.
        menu.Opening += (_, _) => pause.Checked = !_controller.Config.ScheduleEnabled;

        return menu;
    }

    private void ShowWindow()
    {
        if (_window is null || _window.IsDisposed)
        {
            _window = new MainForm(_controller);
            _window.FormClosing += (_, e) =>
            {
                // Closing means "get out of my way", not "stop protecting my sessions".
                if (_exiting || e.CloseReason != CloseReason.UserClosing) return;
                e.Cancel = true;
                _window.Hide();
            };
        }

        _window.Show();
        if (_window.WindowState == FormWindowState.Minimized)
            _window.WindowState = FormWindowState.Normal;
        _window.BringToFront();
        _window.Activate();
    }

    private void OnSessionsChanged()
    {
        if (_window is { IsDisposed: false })
        {
            try { _window.BeginInvoke(UpdateTrayState); }
            catch (InvalidOperationException) { UpdateTrayState(); }
        }
        else
        {
            UpdateTrayState();
        }
    }

    private void UpdateTrayState()
    {
        var total = _controller.Sessions.Count;
        var targets = _controller.Targets().Count;
        var atRisk = _controller.AtRisk().Count;

        _tray.Icon = atRisk > 0 ? _iconAlert : _iconNormal;

        var next = _controller.NextDueUtc is { } due
            ? due.ToLocalTime().ToString("ddd HH:mm")
            : "paused";

        // NotifyIcon.Text throws above 63 characters, so keep this terse.
        var text = $"Necromancer — {targets}/{total} protected, next {next}";
        _tray.Text = text.Length > 63 ? text[..60] + "…" : text;
    }

    private void OnRunCompleted(TouchOutcome outcome)
    {
        if (!_controller.Config.ShowNotifications) return;
        if (outcome.Touched == 0 && !outcome.AnyFailures) return;

        var title = outcome.AnyFailures ? "Some sessions could not be touched" : "Sessions kept alive";
        var body = outcome.AnyFailures
            ? $"{outcome.Touched} touched, {outcome.Failed} failed. See the Activity tab."
            : $"{outcome.Touched} session(s) refreshed" +
              (outcome.Archived > 0 ? $", {outcome.Archived} archived." : ".");

        try
        {
            _tray.ShowBalloonTip(4000, title, body,
                outcome.AnyFailures ? ToolTipIcon.Warning : ToolTipIcon.Info);
        }
        catch { /* balloon tips are suppressible by policy; never fatal */ }
    }

    private void ExitApp()
    {
        _exiting = true;
        Log.Info("Claude Necromancer exiting.");

        _tray.Visible = false;
        _tray.Dispose();
        _window?.Close();
        _controller.Dispose();
        _iconNormal.Dispose();
        _iconAlert.Dispose();

        ExitThread();
    }
}
