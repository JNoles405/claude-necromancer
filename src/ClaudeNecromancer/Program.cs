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

        WriteConsole($"Touching {targets.Count} session(s)…");
        var outcome = controller.RunTouch(manual: true);

        WriteConsole($"Touched {outcome.Touched}, archived {outcome.Archived}, failed {outcome.Failed}.");
        foreach (var error in outcome.Errors) WriteConsole("  " + error);

        Environment.ExitCode = outcome.AnyFailures ? 1 : 0;
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
            _consoleAttached = true;
        }
        Console.WriteLine(line);
    }

    private static bool _consoleAttached;
    private const int AttachParentProcess = -1;

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int processId);
}
