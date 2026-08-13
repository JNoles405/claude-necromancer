using ClaudeNecromancer.Core;
using ClaudeNecromancer.UI;

namespace ClaudeNecromancer;

internal static class Program
{
    /// <summary>Global so a second launch surfaces the running instance instead of duplicating the tray icon.</summary>
    private static Mutex? _instanceMutex;

    [STAThread]
    private static void Main(string[] args)
    {
        // Headless modes, for Task Scheduler and for checking behaviour without a window.
        // These run before the single-instance guard so they work alongside the tray app.
        if (Has(args, "--version"))
        {
            WriteConsole($"Claude Necromancer {VersionInfo.Display()} ({VersionInfo.Plain()})");
            return;
        }

        if (Has(args, "--list"))
        {
            RunList();
            return;
        }

        if (Has(args, "--touch-now"))
        {
            RunHeadlessTouch();
            return;
        }

        if (Has(args, "--update"))
        {
            RunHeadlessUpdate(install: !Has(args, "--check-only"));
            return;
        }

        _instanceMutex = new Mutex(initiallyOwned: true, @"Local\ClaudeNecromancer.SingleInstance", out var isFirst);
        if (!isFirst)
        {
            MessageBox.Show(
                "Claude Necromancer is already running — look for the heartbeat icon in the system tray.",
                "Claude Necromancer", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

        Application.ThreadException += (_, e) =>
        {
            Log.Error($"Unhandled UI exception: {e.Exception}");
            MessageBox.Show($"Something went wrong:\n\n{e.Exception.Message}",
                "Claude Necromancer", MessageBoxButtons.OK, MessageBoxIcon.Error);
        };

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Log.Error($"Unhandled exception: {e.ExceptionObject}");

        var startMinimized = args.Any(a =>
            a.Equals("--minimized", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("/minimized", StringComparison.OrdinalIgnoreCase));

        Log.Info("Claude Necromancer starting.");
        Application.Run(new TrayApp(startMinimized));
    }

    private static bool Has(string[] args, string flag) =>
        args.Any(a => a.Equals(flag, StringComparison.OrdinalIgnoreCase) ||
                      a.Equals("/" + flag.TrimStart('-'), StringComparison.OrdinalIgnoreCase));

    private static void RunList()
    {
        var config = AppConfig.Load();
        var days = SettingsPatcher.GetEffectiveCleanupPeriodDays(out var explicitlySet, out _);
        var sessions = SessionScanner.Scan();

        WriteConsole($"Retention window: {days} days " +
                     $"({(explicitlySet ? "from settings.json" : "Claude Code default")})");
        WriteConsole($"{sessions.Count} session(s) found; mode = {config.Mode}");
        WriteConsole("");
        WriteConsole($"{"DAYS LEFT",10}  {"SIZE",10}  {"LAST WRITE",17}  PROJECT");

        foreach (var s in sessions)
        {
            var left = s.DaysLeft(days);
            WriteConsole($"{(left <= 0 ? "OVERDUE" : left.ToString("0.0")),10}  " +
                         $"{s.TotalBytes / 1024.0 / 1024.0,8:0.##} MB  " +
                         $"{s.LastWriteUtc.ToLocalTime():yyyy-MM-dd HH:mm}  " +
                         $"{s.ShortProject}");
        }
    }

    private static void RunHeadlessTouch()
    {
        using var controller = new AppController();
        var targets = controller.Targets();

        WriteConsole($"Touching {targets.Count} session(s)...");
        var outcome = controller.RunTouch(manual: true);

        WriteConsole($"Touched {outcome.Touched}, archived {outcome.Archived}, failed {outcome.Failed}.");
        foreach (var error in outcome.Errors) WriteConsole("  " + error);

        Environment.ExitCode = outcome.AnyFailures ? 1 : 0;
    }

    /// <summary>
    /// Unattended update. Checks, and unless --check-only is passed, downloads, verifies the
    /// published SHA-256 and swaps the executable.
    ///
    /// The verification is not skippable here either: an unattended path is exactly where a bad
    /// download would go unnoticed, so the same rule applies as in the UI — a hash that does not
    /// match is deleted rather than run.
    /// </summary>
    private static void RunHeadlessUpdate(bool install)
    {
        var updater = new Updater();

        WriteConsole($"Current: {VersionInfo.Display()}");
        updater.CheckAsync().GetAwaiter().GetResult();

        switch (updater.State)
        {
            case UpdateState.UpToDate:
                WriteConsole("Up to date.");
                return;

            case UpdateState.Failed:
                WriteConsole("FAILED: " + updater.Error);
                Environment.ExitCode = 1;
                return;

            case UpdateState.UpdateAvailable:
                var latest = updater.Latest!;
                WriteConsole($"Available: {latest.Version} - {latest.AssetName} " +
                             $"({latest.Bytes / 1024.0 / 1024.0:0.##} MB)");
                WriteConsole($"Expected sha256: {latest.Sha256}");
                break;
        }

        if (!install)
        {
            WriteConsole("Check only; not downloading.");
            return;
        }

        WriteConsole("Downloading...");
        updater.DownloadAsync().GetAwaiter().GetResult();

        if (updater.State != UpdateState.ReadyToInstall)
        {
            WriteConsole("FAILED: " + (updater.Error ?? updater.State.ToString()));
            Environment.ExitCode = 1;
            return;
        }

        WriteConsole("Checksum verified.");
        WriteConsole(updater.InstallAndRestart()
            ? "Installing; this process will exit and the new build will start."
            : "FAILED: could not start the installer.");
    }

    /// <summary>
    /// A WinExe has no console of its own, so attach to the parent's when there is one. Without
    /// this the headless modes would run silently and look like they had done nothing.
    /// </summary>
    private static void WriteConsole(string line)
    {
        if (!_consoleAttached)
        {
            AttachConsole(AttachParentProcess);

            // Without this the console falls back to the OEM code page and any non-ASCII character
            // arrives as a replacement glyph — em dashes and ellipses came through as "?".
            try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { }

            _consoleAttached = true;
        }
        Console.WriteLine(line);
    }

    private static bool _consoleAttached;
    private const int AttachParentProcess = -1;

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int processId);
}
