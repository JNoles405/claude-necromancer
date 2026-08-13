using System.Text.Json;

namespace ClaudeNecromancer.Core;

/// <summary>
/// Keeps a copy of each protected session outside ~/.claude, where the retention sweep never looks.
///
/// This is the belt to touching's braces. Touching depends on the sweep being mtime-driven; an
/// archived copy survives regardless of how Claude Code decides to clean up in future, and also
/// survives someone deleting ~/.claude outright.
/// </summary>
public static class Archiver
{
    /// <summary>
    /// Copies a session into the archive if the archived copy is missing or stale.
    ///
    /// Staleness is judged by SIZE, never by timestamp. That is not an oversight: this app rewrites
    /// mtimes by design, so mtime carries no information about content here. Transcripts are
    /// append-only, so a size match means the content matches.
    /// </summary>
    /// <returns>True if anything was copied.</returns>
    public static bool ArchiveIfChanged(SessionInfo session, string archiveRoot)
    {
        var destDir = Path.Combine(archiveRoot, session.ProjectFolder);
        Directory.CreateDirectory(destDir);

        var destTranscript = Path.Combine(destDir, session.SessionId + ".jsonl");
        var copied = false;

        var src = new FileInfo(session.TranscriptPath);
        var dst = new FileInfo(destTranscript);

        if (!dst.Exists || dst.Length != src.Length)
        {
            File.Copy(session.TranscriptPath, destTranscript, overwrite: true);
            copied = true;
        }

        if (session.SidecarDir is { } sidecar && Directory.Exists(sidecar))
            copied |= CopyTree(sidecar, Path.Combine(destDir, session.SessionId));

        if (session.FileHistoryDir is { } history && Directory.Exists(history))
            copied |= CopyTree(history, Path.Combine(destDir, session.SessionId + ".file-history"));

        if (copied)
        {
            WriteManifest(session, Path.Combine(destDir, session.SessionId + ".meta.json"));
            Log.Info($"Archived {session.ShortProject}/{session.SessionId[..8]} -> {destDir}");
        }

        return copied;
    }

    private static bool CopyTree(string sourceDir, string destDir)
    {
        var copied = false;
        Directory.CreateDirectory(destDir);

        foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(sourceDir, file);
            var dest = Path.Combine(destDir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);

            try
            {
                var s = new FileInfo(file);
                var d = new FileInfo(dest);
                if (!d.Exists || d.Length != s.Length)
                {
                    File.Copy(file, dest, overwrite: true);
                    copied = true;
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"Could not archive {file}: {ex.Message}");
            }
        }

        return copied;
    }

    /// <summary>A human-readable sidecar so an archive folder is navigable months later.</summary>
    private static void WriteManifest(SessionInfo session, string path)
    {
        try
        {
            var manifest = new
            {
                session.SessionId,
                session.ProjectFolder,
                session.Cwd,
                session.Title,
                session.SizeBytes,
                ArchivedAtUtc = DateTime.UtcNow,
                Note = "Archived by Claude Necromancer. This copy lives outside ~/.claude and is "
                     + "not subject to Claude Code's cleanupPeriodDays sweep.",
            };
            File.WriteAllText(path,
                JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not write manifest {path}: {ex.Message}");
        }
    }
}
