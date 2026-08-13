namespace ClaudeNecromancer.Core;

/// <summary>
/// Decides when a touch run is due.
///
/// Rather than arming a timer for the full interval, this ticks once a minute and asks whether the
/// due time has passed. That survives the things a long timer does not: sleep, hibernation, the
/// machine being off over a weekend, and the user changing the system clock.
/// </summary>
public sealed class Scheduler : IDisposable
{
    private static readonly TimeSpan Heartbeat = TimeSpan.FromMinutes(1);

    private readonly System.Threading.Timer _timer;
    private readonly Func<AppConfig> _config;
    private readonly Action _run;
    private int _running;

    public Scheduler(Func<AppConfig> config, Action run)
    {
        _config = config;
        _run = run;
        _timer = new System.Threading.Timer(_ => Tick(), null, Heartbeat, Heartbeat);
    }

    public DateTime? NextDueUtc
    {
        get
        {
            var cfg = _config();
            if (!cfg.ScheduleEnabled) return null;
            return (cfg.LastRunUtc ?? DateTime.UtcNow) + cfg.Interval;
        }
    }

    private void Tick()
    {
        // A slow run (large archive copy) must not stack up behind the heartbeat.
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0) return;

        try
        {
            var cfg = _config();
            if (!cfg.ScheduleEnabled) return;

            var due = (cfg.LastRunUtc ?? DateTime.MinValue) + cfg.Interval;
            if (DateTime.UtcNow >= due) _run();
        }
        catch (Exception ex)
        {
            Log.Error($"Scheduled run failed: {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _running, 0);
        }
    }

    public void Dispose() => _timer.Dispose();
}
