using ClaudeNecromancer.Core;

namespace ClaudeNecromancer.UI;

/// <summary>
/// The window. Built in code rather than with the designer so the whole layout is reviewable as
/// text and diffs cleanly.
/// </summary>
public sealed class MainForm : Form
{
    private readonly AppController _controller;
    private readonly Updater _updater = new();

    // Sessions tab
    private ListView _list = null!;
    private Label _summary = null!;
    private Label _warning = null!;
    private RadioButton _modeAll = null!;
    private RadioButton _modeSelected = null!;
    private Button _touchNow = null!;

    // Schedule tab
    private CheckBox _scheduleEnabled = null!;
    private NumericUpDown _interval = null!;
    private ComboBox _intervalUnit = null!;
    private CheckBox _touchOnStartup = null!;
    private CheckBox _touchSidecars = null!;
    private CheckBox _touchFileHistory = null!;
    private CheckBox _archiveEnabled = null!;
    private TextBox _archiveDir = null!;
    private CheckBox _runAtLogin = null!;
    private CheckBox _startMinimized = null!;
    private CheckBox _notifications = null!;
    private Label _retentionLabel = null!;

    // Chat backup tab
    private TextBox _sessionKey = null!;
    private TextBox _chatDir = null!;
    private CheckBox _chatEnabled = null!;
    private Button _backupNow = null!;
    private TextBox _chatStatus = null!;
    private CancellationTokenSource? _backupCts;

    // Updates tab
    private Label _updateStatus = null!;
    private TextBox _updateNotes = null!;
    private ProgressBar _updateProgress = null!;
    private Button _checkUpdates = null!;
    private Button _downloadUpdate = null!;
    private Button _installUpdate = null!;
    private CheckBox _checkOnStartup = null!;

    // Activity tab
    private TextBox _logBox = null!;

    /// <summary>Suppresses config writes while the controls are being populated from config.</summary>
    private bool _loading;

    private static readonly Color RiskOverdue = Color.FromArgb(178, 34, 34);
    private static readonly Color RiskCritical = Color.FromArgb(200, 80, 20);
    private static readonly Color RiskWarning = Color.FromArgb(150, 110, 0);

    public MainForm(AppController controller)
    {
        _controller = controller;

        Text = $"Claude Necromancer {VersionInfo.Display()}";
        Icon = IconFactory.Create(Color.FromArgb(94, 200, 156));
        MinimumSize = new Size(880, 600);
        Size = new Size(1020, 720);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9f);

        BuildLayout();
        ReloadFromConfig();
        RefreshList();

        _controller.SessionsChanged += OnSessionsChanged;
        _updater.Changed += OnUpdaterChanged;
        Log.LineWritten += OnLogLine;

        FormClosed += (_, _) =>
        {
            _controller.SessionsChanged -= OnSessionsChanged;
            _updater.Changed -= OnUpdaterChanged;
            Log.LineWritten -= OnLogLine;
        };

        if (_controller.Config.CheckForUpdatesOnStartup)
            _ = _updater.CheckAsync();
    }

    // ── Layout ──────────────────────────────────────────────────────────────

    private void BuildLayout()
    {
        var header = BuildHeader();
        var tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(14, 6) };

        tabs.TabPages.Add(BuildSessionsTab());
        tabs.TabPages.Add(BuildScheduleTab());
        tabs.TabPages.Add(BuildChatTab());
        tabs.TabPages.Add(BuildUpdatesTab());
        tabs.TabPages.Add(BuildActivityTab());

        Controls.Add(tabs);
        Controls.Add(header);
    }

    private Panel BuildHeader()
    {
        var panel = new Panel { Dock = DockStyle.Top, Height = 88, Padding = new Padding(16, 12, 16, 8) };

        _summary = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Top,
            Height = 26,
            Font = new Font("Segoe UI", 11f, FontStyle.Bold),
            Text = "Scanning…",
        };

        _warning = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Top,
            Height = 34,
            ForeColor = RiskCritical,
            Text = "",
        };

        _touchNow = new Button
        {
            Text = "Touch now",
            Width = 130,
            Height = 34,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
        };
        _touchNow.Click += (_, _) => RunTouchNow();

        panel.Controls.Add(_warning);
        panel.Controls.Add(_summary);
        panel.Controls.Add(_touchNow);

        panel.Resize += (_, _) => _touchNow.Location = new Point(panel.Width - _touchNow.Width - 16, 14);
        return panel;
    }

    private TabPage BuildSessionsTab()
    {
        var page = new TabPage("Sessions") { Padding = new Padding(12) };

        _list = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            CheckBoxes = true,
            FullRowSelect = true,
            GridLines = false,
            HideSelection = false,
            OwnerDraw = false,
        };
        _list.Columns.Add("Project", 190);
        _list.Columns.Add("What it was about", 380);
        _list.Columns.Add("Size", 80, HorizontalAlignment.Right);
        _list.Columns.Add("Last touched", 120, HorizontalAlignment.Right);
        _list.Columns.Add("Days left", 90, HorizontalAlignment.Right);
        _list.ItemChecked += OnItemChecked;

        var controls = new Panel { Dock = DockStyle.Top, Height = 74 };

        _modeAll = new RadioButton
        {
            Text = "Protect every session (recommended)",
            Location = new Point(2, 4),
            AutoSize = true,
        };
        _modeSelected = new RadioButton
        {
            Text = "Protect only the sessions ticked below",
            Location = new Point(2, 28),
            AutoSize = true,
        };
        _modeAll.CheckedChanged += OnModeChanged;
        _modeSelected.CheckedChanged += OnModeChanged;

        var selectAll = new Button { Text = "Tick all", Location = new Point(2, 50), Width = 90, Height = 24 };
        selectAll.Click += (_, _) => SetAllChecks(true);

        var selectNone = new Button { Text = "Untick all", Location = new Point(98, 50), Width = 90, Height = 24 };
        selectNone.Click += (_, _) => SetAllChecks(false);

        var refresh = new Button { Text = "Rescan", Location = new Point(194, 50), Width = 90, Height = 24 };
        refresh.Click += (_, _) => _controller.Refresh();

        var openFolder = new Button { Text = "Open folder", Location = new Point(290, 50), Width = 110, Height = 24 };
        openFolder.Click += (_, _) => OpenPath(ClaudePaths.ProjectsDir);

        controls.Controls.AddRange(new Control[]
        {
            _modeAll, _modeSelected, selectAll, selectNone, refresh, openFolder,
        });

        page.Controls.Add(_list);
        page.Controls.Add(controls);
        return page;
    }

    private TabPage BuildScheduleTab()
    {
        var page = new TabPage("Schedule & Settings") { Padding = new Padding(16), AutoScroll = true };
        var y = 8;

        _scheduleEnabled = Check("Keep sessions alive on a schedule", ref y, page);

        var row = new Panel { Location = new Point(24, y), Size = new Size(560, 30) };
        row.Controls.Add(new Label { Text = "Touch every", Location = new Point(0, 6), AutoSize = true });
        _interval = new NumericUpDown
        {
            Location = new Point(84, 3), Width = 70, Minimum = 1, Maximum = 720, Value = 12,
        };
        _intervalUnit = new ComboBox
        {
            Location = new Point(162, 3), Width = 90, DropDownStyle = ComboBoxStyle.DropDownList,
        };
        _intervalUnit.Items.AddRange(new object[] { "hours", "days" });
        _interval.ValueChanged += (_, _) => SaveScheduleFields();
        _intervalUnit.SelectedIndexChanged += (_, _) => SaveScheduleFields();
        row.Controls.Add(_interval);
        row.Controls.Add(_intervalUnit);
        page.Controls.Add(row);
        y += 34;

        _touchOnStartup = Check("Also touch when the app starts (catches up after the PC was off)", ref y, page, indent: 24);
        y += 8;

        page.Controls.Add(Section("What gets touched", ref y));
        _touchSidecars = Check("Subagent transcripts and spilled tool results", ref y, page, indent: 24);
        _touchFileHistory = Check("Checkpoint snapshots in file-history (swept separately)", ref y, page, indent: 24);
        y += 8;

        page.Controls.Add(Section("Archive", ref y));
        _archiveEnabled = Check("Also keep a copy outside ~/.claude, where the sweep never looks", ref y, page, indent: 24);

        _archiveDir = new TextBox { Location = new Point(44, y), Width = 480 };
        var browseArchive = new Button { Text = "Browse…", Location = new Point(532, y - 1), Width = 84 };
        browseArchive.Click += (_, _) => BrowseInto(_archiveDir);
        var openArchive = new Button { Text = "Open", Location = new Point(622, y - 1), Width = 64 };
        openArchive.Click += (_, _) => OpenPath(_archiveDir.Text);
        _archiveDir.TextChanged += (_, _) => SaveScheduleFields();
        page.Controls.Add(_archiveDir);
        page.Controls.Add(browseArchive);
        page.Controls.Add(openArchive);
        y += 38;

        page.Controls.Add(Section("Claude Code retention window", ref y));
        _retentionLabel = new Label
        {
            Location = new Point(24, y), Size = new Size(660, 46), AutoSize = false,
        };
        page.Controls.Add(_retentionLabel);
        y += 50;

        var raise = new Button
        {
            Text = "Raise retention to 10 years", Location = new Point(24, y), Width = 210, Height = 28,
        };
        raise.Click += (_, _) => RaiseRetention();
        var openSettings = new Button
        {
            Text = "Open settings.json", Location = new Point(242, y), Width = 150, Height = 28,
        };
        openSettings.Click += (_, _) => OpenPath(ClaudePaths.SettingsJson);
        page.Controls.Add(raise);
        page.Controls.Add(openSettings);
        y += 40;

        page.Controls.Add(Section("Application", ref y));
        _runAtLogin = Check("Start with Windows", ref y, page, indent: 24);
        _startMinimized = Check("Start minimised to the tray", ref y, page, indent: 24);
        _notifications = Check("Show a notification after each run", ref y, page, indent: 24);

        foreach (var c in new[]
                 {
                     _scheduleEnabled, _touchOnStartup, _touchSidecars, _touchFileHistory,
                     _archiveEnabled, _startMinimized, _notifications,
                 })
        {
            c.CheckedChanged += (_, _) => SaveScheduleFields();
        }

        _runAtLogin.CheckedChanged += (_, _) =>
        {
            if (_loading) return;
            StartupRegistration.Set(_runAtLogin.Checked);
            SaveScheduleFields();
        };

        return page;
    }

    private TabPage BuildChatTab()
    {
        var page = new TabPage("Chat Backup") { Padding = new Padding(16), AutoScroll = true };
        var y = 8;

        var explain = new Label
        {
            Location = new Point(4, y),
            Size = new Size(940, 92),
            Text =
                "claude.ai conversations are stored on Anthropic's servers and are NOT deleted for being idle — "
              + "they stay until you delete them. So there is nothing to keep alive here, and \"touching\" a web "
              + "chat would mean posting real messages into it.\n\n"
              + "What is worth doing is keeping your own copy. This downloads every conversation to disk as JSON "
              + "and readable Markdown. It only ever reads; it never writes to your account.",
        };
        page.Controls.Add(explain);
        y += 100;

        _chatEnabled = Check("Back up conversations on the same schedule as sessions", ref y, page);
        y += 6;

        page.Controls.Add(new Label
        {
            Location = new Point(4, y), AutoSize = true,
            Text = "claude.ai session key",
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
        });
        y += 22;

        page.Controls.Add(new Label
        {
            Location = new Point(4, y),
            Size = new Size(940, 50),
            Text =
                "In a browser signed in to claude.ai, press F12 → Application → Cookies → https://claude.ai, and "
              + "copy the value of \"sessionKey\". It is a live credential, so it is stored encrypted with Windows "
              + "DPAPI and is readable only by your Windows account on this machine.",
        });
        y += 54;

        _sessionKey = new TextBox { Location = new Point(4, y), Width = 620, UseSystemPasswordChar = true };
        var saveKey = new Button { Text = "Save key", Location = new Point(632, y - 1), Width = 90 };
        saveKey.Click += (_, _) => SaveSessionKey();
        var showKey = new CheckBox { Text = "Show", Location = new Point(730, y), AutoSize = true };
        showKey.CheckedChanged += (_, _) => _sessionKey.UseSystemPasswordChar = !showKey.Checked;
        page.Controls.Add(_sessionKey);
        page.Controls.Add(saveKey);
        page.Controls.Add(showKey);
        y += 38;

        page.Controls.Add(new Label { Location = new Point(4, y), AutoSize = true, Text = "Save backups to" });
        y += 20;
        _chatDir = new TextBox { Location = new Point(4, y), Width = 620 };
        _chatDir.TextChanged += (_, _) => SaveScheduleFields();
        var browseChat = new Button { Text = "Browse…", Location = new Point(632, y - 1), Width = 90 };
        browseChat.Click += (_, _) => BrowseInto(_chatDir);
        var openChat = new Button { Text = "Open", Location = new Point(730, y - 1), Width = 64 };
        openChat.Click += (_, _) => OpenPath(_chatDir.Text);
        page.Controls.Add(_chatDir);
        page.Controls.Add(browseChat);
        page.Controls.Add(openChat);
        y += 40;

        _backupNow = new Button { Text = "Back up conversations now", Location = new Point(4, y), Width = 210, Height = 30 };
        _backupNow.Click += (_, _) => RunChatBackup();
        page.Controls.Add(_backupNow);
        y += 40;

        _chatStatus = new TextBox
        {
            Location = new Point(4, y),
            Size = new Size(940, 150),
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
            Font = new Font("Consolas", 8.5f),
        };
        page.Controls.Add(_chatStatus);

        return page;
    }

    private TabPage BuildUpdatesTab()
    {
        var page = new TabPage("Updates") { Padding = new Padding(16) };
        var y = 8;

        page.Controls.Add(new Label
        {
            Location = new Point(4, y), AutoSize = true,
            Font = new Font("Segoe UI", 11f, FontStyle.Bold),
            Text = $"Claude Necromancer {VersionInfo.Display()}",
        });
        y += 30;

        _updateStatus = new Label { Location = new Point(4, y), Size = new Size(900, 40) };
        page.Controls.Add(_updateStatus);
        y += 44;

        _checkUpdates = new Button { Text = "Check for updates", Location = new Point(4, y), Width = 150, Height = 30 };
        _checkUpdates.Click += (_, _) => _ = _updater.CheckAsync();

        _downloadUpdate = new Button { Text = "Download", Location = new Point(162, y), Width = 120, Height = 30, Enabled = false };
        _downloadUpdate.Click += (_, _) => _ = _updater.DownloadAsync();

        _installUpdate = new Button { Text = "Install and restart", Location = new Point(290, y), Width = 150, Height = 30, Enabled = false };
        _installUpdate.Click += (_, _) => InstallUpdate();

        page.Controls.AddRange(new Control[] { _checkUpdates, _downloadUpdate, _installUpdate });
        y += 38;

        _updateProgress = new ProgressBar { Location = new Point(4, y), Size = new Size(436, 8), Visible = false };
        page.Controls.Add(_updateProgress);
        y += 18;

        _checkOnStartup = new CheckBox
        {
            Text = "Check for updates when the app starts", Location = new Point(4, y), AutoSize = true,
        };
        _checkOnStartup.CheckedChanged += (_, _) =>
        {
            if (_loading) return;
            _controller.Config.CheckForUpdatesOnStartup = _checkOnStartup.Checked;
            _controller.Config.Save();
        };
        page.Controls.Add(_checkOnStartup);
        y += 30;

        page.Controls.Add(new Label
        {
            Location = new Point(4, y), AutoSize = true, Text = "Release notes",
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
        });
        y += 22;

        _updateNotes = new TextBox
        {
            Location = new Point(4, y),
            Size = new Size(940, 220),
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
        };
        page.Controls.Add(_updateNotes);

        return page;
    }

    private TabPage BuildActivityTab()
    {
        var page = new TabPage("Activity") { Padding = new Padding(12) };

        _logBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Font = new Font("Consolas", 8.5f),
            Text = string.Join(Environment.NewLine, Log.Tail(400)),
        };

        var bar = new Panel { Dock = DockStyle.Bottom, Height = 36 };
        var openLog = new Button { Text = "Open log file", Location = new Point(0, 4), Width = 120, Height = 26 };
        openLog.Click += (_, _) => OpenPath(ClaudePaths.LogPath);
        bar.Controls.Add(openLog);

        page.Controls.Add(_logBox);
        page.Controls.Add(bar);
        return page;
    }

    // ── Small layout helpers ────────────────────────────────────────────────

    private static CheckBox Check(string text, ref int y, Control parent, int indent = 4)
    {
        var box = new CheckBox { Text = text, Location = new Point(indent, y), AutoSize = true };
        parent.Controls.Add(box);
        y += 26;
        return box;
    }

    private static Label Section(string text, ref int y)
    {
        var label = new Label
        {
            Text = text,
            Location = new Point(4, y),
            AutoSize = true,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
        };
        y += 24;
        return label;
    }

    // ── State ───────────────────────────────────────────────────────────────

    public void ReloadFromConfig()
    {
        if (InvokeRequired) { BeginInvoke(ReloadFromConfig); return; }

        _loading = true;
        var c = _controller.Config;

        _modeAll.Checked = c.Mode == ProtectionMode.All;
        _modeSelected.Checked = c.Mode == ProtectionMode.Selected;

        _scheduleEnabled.Checked = c.ScheduleEnabled;
        if (c.IntervalHours >= 24 && Math.Abs(c.IntervalHours % 24) < 0.001)
        {
            _intervalUnit.SelectedItem = "days";
            _interval.Value = Math.Clamp((decimal)(c.IntervalHours / 24), 1, 720);
        }
        else
        {
            _intervalUnit.SelectedItem = "hours";
            _interval.Value = Math.Clamp((decimal)c.IntervalHours, 1, 720);
        }

        _touchOnStartup.Checked = c.TouchOnStartup;
        _touchSidecars.Checked = c.TouchSidecars;
        _touchFileHistory.Checked = c.TouchFileHistory;
        _archiveEnabled.Checked = c.ArchiveEnabled;
        _archiveDir.Text = c.ArchiveDir;
        _startMinimized.Checked = c.StartMinimized;
        _notifications.Checked = c.ShowNotifications;
        _runAtLogin.Checked = StartupRegistration.IsRegistered();

        _chatEnabled.Checked = c.ChatBackupEnabled;
        _chatDir.Text = c.ChatBackupDir;
        _sessionKey.Text = DpapiSecret.Unprotect(c.ProtectedSessionKey) ?? "";

        _checkOnStartup.Checked = c.CheckForUpdatesOnStartup;

        _loading = false;
        UpdateRetentionLabel();
    }

    private void SaveScheduleFields()
    {
        if (_loading) return;
        var c = _controller.Config;

        c.ScheduleEnabled = _scheduleEnabled.Checked;
        c.IntervalHours = (double)_interval.Value * ((string?)_intervalUnit.SelectedItem == "days" ? 24 : 1);
        c.TouchOnStartup = _touchOnStartup.Checked;
        c.TouchSidecars = _touchSidecars.Checked;
        c.TouchFileHistory = _touchFileHistory.Checked;
        c.ArchiveEnabled = _archiveEnabled.Checked;
        c.ArchiveDir = _archiveDir.Text.Trim();
        c.StartMinimized = _startMinimized.Checked;
        c.ShowNotifications = _notifications.Checked;
        c.RunAtLogin = _runAtLogin.Checked;
        c.ChatBackupEnabled = _chatEnabled.Checked;
        c.ChatBackupDir = _chatDir.Text.Trim();

        c.Save();
        UpdateSummary();
    }

    private void OnModeChanged(object? sender, EventArgs e)
    {
        if (_loading) return;
        _controller.Config.Mode = _modeAll.Checked ? ProtectionMode.All : ProtectionMode.Selected;
        _controller.Config.Save();
        RefreshList();
    }

    private void OnItemChecked(object? sender, ItemCheckedEventArgs e)
    {
        if (_loading) return;
        if (e.Item.Tag is not SessionInfo session) return;

        if (e.Item.Checked) _controller.Config.SelectedSessionIds.Add(session.SessionId);
        else _controller.Config.SelectedSessionIds.Remove(session.SessionId);

        _controller.Config.Save();
        UpdateSummary();
    }

    private void SetAllChecks(bool value)
    {
        _loading = true;
        foreach (ListViewItem item in _list.Items)
        {
            item.Checked = value;
            if (item.Tag is not SessionInfo s) continue;
            if (value) _controller.Config.SelectedSessionIds.Add(s.SessionId);
            else _controller.Config.SelectedSessionIds.Remove(s.SessionId);
        }
        _loading = false;

        _controller.Config.Save();
        UpdateSummary();
    }

    private void OnSessionsChanged()
    {
        if (InvokeRequired) { BeginInvoke(OnSessionsChanged); return; }
        RefreshList();
    }

    private void RefreshList()
    {
        _loading = true;
        _list.BeginUpdate();
        _list.Items.Clear();

        var days = _controller.CleanupPeriodDays;
        var protectAll = _controller.Config.Mode == ProtectionMode.All;

        foreach (var s in _controller.Sessions)
        {
            var left = s.DaysLeft(days);
            var item = new ListViewItem(s.ShortProject) { Tag = s };
            item.SubItems.Add(s.Title);
            item.SubItems.Add(FormatSize(s.TotalBytes));
            item.SubItems.Add(s.LastWriteUtc.ToLocalTime().ToString("dd MMM HH:mm"));
            item.SubItems.Add(left <= 0 ? "overdue" : $"{left:0.#}");

            item.ForeColor = s.Risk(days) switch
            {
                RiskLevel.Overdue => RiskOverdue,
                RiskLevel.Critical => RiskCritical,
                RiskLevel.Warning => RiskWarning,
                _ => SystemColors.ControlText,
            };

            item.ToolTipText = $"{s.DisplayProject}\n{s.TranscriptPath}";
            item.Checked = protectAll || _controller.Config.SelectedSessionIds.Contains(s.SessionId);
            _list.Items.Add(item);
        }

        // In "protect everything" mode the ticks are informational, so freeze them.
        _list.Enabled = !protectAll || true;
        foreach (ListViewItem item in _list.Items) item.Checked = protectAll || item.Checked;

        _list.EndUpdate();
        _loading = false;

        UpdateSummary();
        UpdateRetentionLabel();
    }

    private void UpdateSummary()
    {
        var total = _controller.Sessions.Count;
        var targets = _controller.Targets().Count;
        var bytes = _controller.Sessions.Sum(s => s.TotalBytes);
        var atRisk = _controller.AtRisk();

        var next = _controller.NextDueUtc is { } due && _controller.Config.ScheduleEnabled
            ? due.ToLocalTime().ToString("ddd dd MMM, HH:mm")
            : "schedule paused";

        _summary.Text = $"{total} session{(total == 1 ? "" : "s")} · {targets} protected · " +
                        $"{FormatSize(bytes)} on disk · next run: {next}";

        _warning.Text = atRisk.Count == 0
            ? $"Nothing within {_controller.CleanupPeriodDays - 7} days of the sweep."
            : $"⚠  {atRisk.Count} protected session{(atRisk.Count == 1 ? " is" : "s are")} within a week of " +
              $"deletion. The oldest is \"{Trim(atRisk[0].ShortProject, 40)}\".";

        _warning.ForeColor = atRisk.Count == 0 ? SystemColors.GrayText : RiskCritical;
    }

    private void UpdateRetentionLabel()
    {
        if (_retentionLabel is null) return;

        var days = _controller.CleanupPeriodDays;
        var source = _controller.CleanupPeriodExplicit
            ? "set in ~/.claude/settings.json"
            : "Claude Code's default, because settings.json does not set it";

        var text = $"Claude Code deletes sessions older than {days} days at startup ({source}).\n" +
                   "Raising this is the root-cause fix — touching only ever buys another window.";

        if (_controller.SettingsProblem is { } problem)
            text = problem + "\n" + text;

        _retentionLabel.Text = text;
    }

    // ── Actions ─────────────────────────────────────────────────────────────

    private void RunTouchNow()
    {
        _touchNow.Enabled = false;
        _touchNow.Text = "Touching…";

        Task.Run(() => _controller.RunTouch(manual: true)).ContinueWith(t =>
        {
            BeginInvoke(() =>
            {
                _touchNow.Enabled = true;
                _touchNow.Text = "Touch now";

                if (t.Result.AnyFailures)
                {
                    MessageBox.Show(this,
                        $"{t.Result.Touched} session(s) touched, {t.Result.Failed} failed.\n\n" +
                        string.Join("\n", t.Result.Errors.Take(10)),
                        "Some sessions could not be touched",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            });
        });
    }

    private void RaiseRetention()
    {
        const int TenYears = 3650;
        var answer = MessageBox.Show(this,
            $"This edits ~/.claude/settings.json and sets cleanupPeriodDays to {TenYears}.\n\n" +
            "Claude Code will then stop deleting sessions on age, which is the real fix — touching " +
            "only ever buys another window.\n\n" +
            "Your current settings.json is backed up first. Note that transcripts hold whatever " +
            "passed through a tool, including secrets, so keeping them for ten years is a " +
            "deliberate trade.\n\nGo ahead?",
            "Raise Claude Code's retention window",
            MessageBoxButtons.YesNo, MessageBoxIcon.Question);

        if (answer != DialogResult.Yes) return;

        try
        {
            SettingsPatcher.SetCleanupPeriodDays(TenYears);
            _controller.Refresh();
            MessageBox.Show(this,
                $"Done — cleanupPeriodDays is now {TenYears}.\n\n" +
                "It takes effect the next time Claude Code starts.",
                "Retention raised", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not update settings.json:\n\n{ex.Message}",
                "Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void SaveSessionKey()
    {
        var key = _sessionKey.Text.Trim();
        _controller.Config.ProtectedSessionKey = string.IsNullOrEmpty(key) ? null : DpapiSecret.Protect(key);
        _controller.Config.Save();
        _chatStatus.AppendText(string.IsNullOrEmpty(key)
            ? "Session key cleared." + Environment.NewLine
            : "Session key saved (encrypted with Windows DPAPI)." + Environment.NewLine);
    }

    private void RunChatBackup()
    {
        if (_backupCts is not null)
        {
            _backupCts.Cancel();
            return;
        }

        var key = _sessionKey.Text.Trim();
        if (string.IsNullOrEmpty(key))
        {
            MessageBox.Show(this, "Paste your claude.ai session key first.",
                "No session key", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        SaveScheduleFields();
        _backupCts = new CancellationTokenSource();
        _backupNow.Text = "Cancel";
        _chatStatus.AppendText($"--- backup started {DateTime.Now:HH:mm:ss} ---{Environment.NewLine}");

        var progress = new Progress<string>(line =>
            _chatStatus.AppendText(line + Environment.NewLine));

        var dir = string.IsNullOrWhiteSpace(_chatDir.Text)
            ? ClaudePaths.DefaultChatBackupDir
            : _chatDir.Text.Trim();

        Task.Run(() => ChatBackup.RunAsync(key, dir, progress, _backupCts.Token))
            .ContinueWith(t => BeginInvoke(() =>
            {
                _backupCts?.Dispose();
                _backupCts = null;
                _backupNow.Text = "Back up conversations now";

                var r = t.IsFaulted
                    ? new ChatBackupResult(0, 0, dir, t.Exception?.GetBaseException().Message)
                    : t.Result;

                _chatStatus.AppendText(r.Success
                    ? $"Done: {r.Written} of {r.Conversations} conversations saved to {r.Destination}{Environment.NewLine}"
                    : $"FAILED: {r.Error}{Environment.NewLine}");
            }));
    }

    private void OnUpdaterChanged()
    {
        if (IsDisposed) return;
        try { BeginInvoke(ApplyUpdaterState); } catch (InvalidOperationException) { }
    }

    private void ApplyUpdaterState()
    {
        var latest = _updater.Latest;

        _checkUpdates.Enabled = _updater.State is not (UpdateState.Checking or UpdateState.Downloading);
        _downloadUpdate.Enabled = _updater.State == UpdateState.UpdateAvailable;
        _installUpdate.Enabled = _updater.State == UpdateState.ReadyToInstall;
        _updateProgress.Visible = _updater.State == UpdateState.Downloading;
        _updateProgress.Value = (int)Math.Clamp(_updater.Progress * 100, 0, 100);

        _updateStatus.Text = _updater.State switch
        {
            UpdateState.Checking => "Checking GitHub for a newer release…",
            UpdateState.UpToDate => $"You are on the latest release ({VersionInfo.Display()}).",
            UpdateState.UpdateAvailable =>
                $"{latest?.Version} is available ({latest?.Bytes / 1024:N0} KB). " +
                "Download it to verify the checksum before installing.",
            UpdateState.Downloading => $"Downloading {latest?.AssetName}… {_updater.Progress:P0}",
            UpdateState.ReadyToInstall =>
                $"{latest?.Version} downloaded and its SHA-256 matches the release. Ready to install.",
            UpdateState.Failed => "⚠  " + _updater.Error,
            _ => "",
        };

        _updateStatus.ForeColor = _updater.State == UpdateState.Failed ? RiskOverdue : SystemColors.ControlText;

        if (latest is not null && _updater.State != UpdateState.UpToDate)
            _updateNotes.Text = latest.Notes.Replace("\n", Environment.NewLine);

        _controller.Config.LastUpdateCheckUtc = DateTime.UtcNow;
    }

    private void InstallUpdate()
    {
        var answer = MessageBox.Show(this,
            $"Claude Necromancer will close, replace itself with {_updater.Latest?.Version} and start again.\n\n" +
            "The download has already been checked against the SHA-256 published with the release. " +
            "The current build is kept alongside as a .bak file.\n\nInstall now?",
            "Install update", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

        if (answer != DialogResult.Yes) return;

        if (_updater.InstallAndRestart()) Application.Exit();
    }

    private void OnLogLine(string line)
    {
        if (IsDisposed || _logBox is null) return;
        try
        {
            BeginInvoke(() =>
            {
                _logBox.AppendText(line + Environment.NewLine);
                _logBox.SelectionStart = _logBox.TextLength;
                _logBox.ScrollToCaret();
            });
        }
        catch (InvalidOperationException) { }
    }

    // ── Utilities ───────────────────────────────────────────────────────────

    private void BrowseInto(TextBox box)
    {
        using var dialog = new FolderBrowserDialog { SelectedPath = box.Text };
        if (dialog.ShowDialog(this) == DialogResult.OK) box.Text = dialog.SelectedPath;
    }

    private static void OpenPath(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path)
                { UseShellExecute = true });
            }
            else if (File.Exists(path))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe")
                { Arguments = $"/select,\"{path}\"", UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not open {path}: {ex.Message}");
        }
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):0.#} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):0.##} GB",
    };

    private static string Trim(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}
