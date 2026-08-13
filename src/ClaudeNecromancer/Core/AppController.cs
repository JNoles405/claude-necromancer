namespace ClaudeNecromancer.Core;

/// <summary>
/// Shared state and behaviour for the tray icon and the window. Both drive the same instance so a
/// touch triggered from the tray is reflected in the list, and vice versa.
/// </summary>
public sealed class AppController : IDisposable
{
    private readonly Scheduler _scheduler;

    public AppConfig Config { get; private set; }
    public List<SessionInfo> Sessions { get; private set; } = new();

    /// <summary>User-level retention window from settings.json, or the documented default of 30.</summary>
    public int CleanupPeriodDays { get; private set; } = ClaudePaths.DefaultCleanupPeriodDays;

    public bool CleanupPeriodExplicit { get; private set; }

    /// <summary>Set when settings.json is unparseable — worth telling the user about.</summary>
    public string? SettingsProblem { get; private set; }

    public event Action? SessionsChanged;
    public event Action<TouchOutcome>? RunCompleted;

    public AppController()
    {
        Config = AppConfig.Load();
        Refresh();
        _scheduler = new Scheduler(() => Config, () => RunTouch(manual: false));
    }

    public DateTime? NextDueUtc => _scheduler.NextDueUtc;

    public void Refresh()
    {
        CleanupPeriodDays = SettingsPatcher.GetEffectiveCleanupPeriodDays(
            out var explicitlySet, out var problem);
        CleanupPeriodExplicit = explicitlySet;
        SettingsProblem = problem;

        Sessions = SessionScanner.Scan();
        SessionsChanged?.Invoke();
    }

    /// <summary>Sessions the current configuration says to keep alive.</summary>
    public List<SessionInfo> Targets() => Config.Filter(Sessions);

    /// <summary>Sessions within a week of the sweep, among those we're actually protecting.</summary>
    public List<SessionInfo> AtRisk() =>
        Targets().Where(s => s.Risk(CleanupPeriodDays) >= RiskLevel.Warning).ToList();

    public TouchOutcome RunTouch(bool manual)
    {
        var targets = Targets();
        Log.Info($"{(manual ? "Manual" : "Scheduled")} run starting: {targets.Count} session(s).");

        var outcome = Toucher.TouchAll(targets, Config);

        Config.LastRunUtc = DateTime.UtcNow;
        Config.Save();

        Log.Info($"Run complete: {outcome.Touched} touched, {outcome.Archived} archived, " +
                 $"{outcome.Failed} failed.");

        if (Config.ChatBackupEnabled) RunChatBackupInBackground();

        SessionsChanged?.Invoke();
        RunCompleted?.Invoke(outcome);
        return outcome;
    }

    /// <summary>
    /// Rides along with the scheduled run when chat backup is switched on. Fire-and-forget: a
    /// claude.ai outage or an expired cookie must never stop sessions being touched, which is the
    /// job that actually has a deadline.
    /// </summary>
    private void RunChatBackupInBackground()
    {
        var key = DpapiSecret.Unprotect(Config.ProtectedSessionKey);
        if (string.IsNullOrWhiteSpace(key))
        {
            Log.Warn("Chat backup is enabled but no session key is stored; skipping.");
            return;
        }

        var dir = Config.ChatBackupDir;
        _ = Task.Run(async () =>
        {
            var result = await ChatBackup.RunAsync(key, dir);
            if (!result.Success) Log.Warn($"Chat backup failed: {result.Error}");
        });
    }

    public void Dispose()
    {
        _scheduler.Dispose();
        Config.Save();
    }
}
