namespace ClaudeNecromancer.Core;

public sealed record TouchOutcome(
    int Touched,
    int Failed,
    int Archived,
    List<string> Errors)
{
    public bool AnyFailures => Failed > 0 || Errors.Count > 0;
}

/// <summary>
/// The heart of the thing. A "touch" moves a file's last-write time to now so that Claude Code's
/// startup sweep sees it as fresh.
///
/// It writes ZERO bytes. That is deliberate and it matters: transcripts are JSONL, parsed one
/// object per line, so appending filler — a bare "." especially — would corrupt the session and
/// destroy the very thing we're trying to preserve. Metadata is the whole job.
/// </summary>
public static class Toucher
{
    public static TouchOutcome TouchAll(IEnumerable<SessionInfo> sessions, AppConfig config)
    {
        var touched = 0;
        var failed = 0;
        var archived = 0;
        var errors = new List<string>();
        var now = DateTime.UtcNow;

        foreach (var session in sessions)
        {
            try
            {
                Touch(session, config, now);
                session.LastTouchedUtc = now;
                session.LastWriteUtc = now;
                session.Protected = true;
                touched++;

                if (config.ArchiveEnabled)
                {
                    if (Archiver.ArchiveIfChanged(session, config.ArchiveDir)) archived++;
                }
            }
            catch (Exception ex)
            {
                failed++;
                var msg = $"{session.ShortProject}/{session.SessionId[..8]}: {ex.Message}";
                errors.Add(msg);
                Log.Error($"Touch failed for {msg}");
            }
        }

        return new TouchOutcome(touched, failed, archived, errors);
    }

    /// <summary>
    /// Refreshes one session's clock. The parent transcript is the file the sweep actually judges —
    /// per the docs, the sidecar directory is "removed with the parent session transcript when it
    /// ages out". We still touch the sidecars, because it costs nothing and means the session
    /// survives even if that behaviour changes.
    ///
    /// file-history/&lt;session&gt;/ is the exception that genuinely needs its own touch: it sits on a
    /// separate swept path and is not tied to the parent transcript.
    /// </summary>
    public static void Touch(SessionInfo session, AppConfig config, DateTime nowUtc)
    {
        TouchFile(session.TranscriptPath, nowUtc);

        if (config.TouchSidecars && session.SidecarDir is { } sidecar)
            TouchTree(sidecar, nowUtc);

        if (config.TouchFileHistory && session.FileHistoryDir is { } history)
            TouchTree(history, nowUtc);
    }

    private static void TouchFile(string path, DateTime nowUtc)
    {
        // Clearing the read-only bit and putting it back keeps archived/backed-up copies touchable.
        var attrs = File.GetAttributes(path);
        var wasReadOnly = attrs.HasFlag(FileAttributes.ReadOnly);
        if (wasReadOnly) File.SetAttributes(path, attrs & ~FileAttributes.ReadOnly);

        try
        {
            File.SetLastWriteTimeUtc(path, nowUtc);
            File.SetLastAccessTimeUtc(path, nowUtc);
        }
        finally
        {
            if (wasReadOnly) File.SetAttributes(path, attrs);
        }
    }

    private static void TouchTree(string dir, DateTime nowUtc)
    {
        if (!Directory.Exists(dir)) return;

        foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
        {
            try { TouchFile(file, nowUtc); }
            catch (Exception ex) { Log.Warn($"Could not touch {file}: {ex.Message}"); }
        }

        foreach (var sub in Directory.EnumerateDirectories(dir, "*", SearchOption.AllDirectories))
        {
            try { Directory.SetLastWriteTimeUtc(sub, nowUtc); } catch { }
        }

        try { Directory.SetLastWriteTimeUtc(dir, nowUtc); } catch { }
    }
}
