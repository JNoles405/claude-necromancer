namespace ClaudeNecromancer.Core;

/// <summary>One Claude Code session: the transcript plus everything that ages out alongside it.</summary>
public sealed class SessionInfo
{
    /// <summary>Session GUID. Stable across project renames, so this is what we key selections on.</summary>
    public required string SessionId { get; init; }

    /// <summary>Raw folder name under ~/.claude/projects, e.g. "F--Claude-Necro".</summary>
    public required string ProjectFolder { get; init; }

    /// <summary>Real working directory, recovered from the transcript's "cwd" field where available.</summary>
    public string? Cwd { get; set; }

    /// <summary>Full path to &lt;session&gt;.jsonl — the file the sweep actually judges.</summary>
    public required string TranscriptPath { get; init; }

    /// <summary>
    /// ~/.claude/projects/&lt;project&gt;/&lt;session&gt;/ — subagents and spilled tool results.
    /// Removed WITH the parent transcript, so protecting the parent protects these too.
    /// </summary>
    public string? SidecarDir { get; set; }

    /// <summary>~/.claude/file-history/&lt;session&gt;/ — swept independently of the transcript.</summary>
    public string? FileHistoryDir { get; set; }

    /// <summary>First user prompt (or the session summary), used as a human-readable label.</summary>
    public string Title { get; set; } = "";

    public long SizeBytes { get; set; }

    /// <summary>Bytes held in the sidecar and file-history directories that ride along with this session.</summary>
    public long SidecarBytes { get; set; }

    public DateTime LastWriteUtc { get; set; }

    /// <summary>True when we've touched this session at least once, i.e. its mtime is our doing.</summary>
    public bool Protected { get; set; }

    public DateTime? LastTouchedUtc { get; set; }

    public long TotalBytes => SizeBytes + SidecarBytes;

    public double AgeDays => (DateTime.UtcNow - LastWriteUtc).TotalDays;

    /// <summary>Days before the sweep is entitled to delete this. Negative means it is already overdue.</summary>
    public double DaysLeft(int cleanupPeriodDays) => cleanupPeriodDays - AgeDays;

    public RiskLevel Risk(int cleanupPeriodDays)
    {
        var left = DaysLeft(cleanupPeriodDays);
        if (left <= 0) return RiskLevel.Overdue;
        if (left <= 3) return RiskLevel.Critical;
        if (left <= 7) return RiskLevel.Warning;
        return RiskLevel.Safe;
    }

    public string DisplayProject =>
        !string.IsNullOrWhiteSpace(Cwd) ? Cwd! : ClaudePaths.PrettifyProjectFolder(ProjectFolder);

    public string ShortProject
    {
        get
        {
            var p = DisplayProject;
            var i = p.LastIndexOfAny(new[] { '\\', '/' });
            return i >= 0 && i < p.Length - 1 ? p[(i + 1)..] : p;
        }
    }
}

public enum RiskLevel
{
    Safe,
    Warning,
    Critical,
    Overdue
}
