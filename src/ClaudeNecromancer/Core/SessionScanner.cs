using System.Text.Json;

namespace ClaudeNecromancer.Core;

/// <summary>Finds every Claude Code session on disk and reads just enough of each to label it.</summary>
public static class SessionScanner
{
    /// <summary>
    /// How far into a transcript we'll read looking for a title. Transcripts run to megabytes;
    /// the opening user prompt is always in the first handful of lines.
    /// </summary>
    private const int TitleScanLines = 60;

    public static List<SessionInfo> Scan()
    {
        var results = new List<SessionInfo>();
        var projectsDir = ClaudePaths.ProjectsDir;
        if (!Directory.Exists(projectsDir)) return results;

        foreach (var projectDir in Directory.EnumerateDirectories(projectsDir))
        {
            var projectFolder = Path.GetFileName(projectDir);

            // Top level only. Nested *.jsonl files are subagent transcripts, which the sweep
            // removes with their parent rather than judging on their own age.
            IEnumerable<string> transcripts;
            try
            {
                transcripts = Directory.EnumerateFiles(projectDir, "*.jsonl", SearchOption.TopDirectoryOnly);
            }
            catch (UnauthorizedAccessException) { continue; }

            foreach (var transcript in transcripts)
            {
                try
                {
                    results.Add(Load(transcript, projectFolder));
                }
                catch (Exception ex)
                {
                    Log.Warn($"Could not read session {transcript}: {ex.Message}");
                }
            }
        }

        return results
            .OrderBy(s => s.LastWriteUtc)
            .ToList();
    }

    private static SessionInfo Load(string transcriptPath, string projectFolder)
    {
        var fi = new FileInfo(transcriptPath);
        var sessionId = Path.GetFileNameWithoutExtension(transcriptPath);

        var info = new SessionInfo
        {
            SessionId = sessionId,
            ProjectFolder = projectFolder,
            TranscriptPath = transcriptPath,
            SizeBytes = fi.Length,
            LastWriteUtc = fi.LastWriteTimeUtc,
        };

        var sidecar = Path.Combine(Path.GetDirectoryName(transcriptPath)!, sessionId);
        if (Directory.Exists(sidecar))
        {
            info.SidecarDir = sidecar;
            info.SidecarBytes += DirectorySize(sidecar);
        }

        var history = Path.Combine(ClaudePaths.FileHistoryDir, sessionId);
        if (Directory.Exists(history))
        {
            info.FileHistoryDir = history;
            info.SidecarBytes += DirectorySize(history);
        }

        ReadHeader(transcriptPath, info);
        return info;
    }

    /// <summary>Pulls a title and the real cwd out of the opening lines of a transcript.</summary>
    private static void ReadHeader(string path, SessionInfo info)
    {
        try
        {
            using var reader = new StreamReader(path);
            var summary = (string?)null;
            var firstPrompt = (string?)null;

            for (var i = 0; i < TitleScanLines; i++)
            {
                var line = reader.ReadLine();
                if (line is null) break;
                if (line.Length == 0) continue;

                JsonElement root;
                try
                {
                    root = JsonDocument.Parse(line).RootElement;
                }
                catch (JsonException) { continue; }
                if (root.ValueKind != JsonValueKind.Object) continue;

                if (info.Cwd is null &&
                    root.TryGetProperty("cwd", out var cwd) &&
                    cwd.ValueKind == JsonValueKind.String)
                {
                    info.Cwd = cwd.GetString();
                }

                var type = root.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String
                    ? t.GetString()
                    : null;

                // A /compact summary is the best label there is — take it and stop.
                if (type == "summary" &&
                    root.TryGetProperty("summary", out var s) &&
                    s.ValueKind == JsonValueKind.String)
                {
                    summary = s.GetString();
                    break;
                }

                if (firstPrompt is null && type == "user" && !IsMeta(root))
                {
                    var text = ExtractText(root);
                    if (IsUsableTitle(text)) firstPrompt = text;
                }

                // Once we have both a prompt and the cwd there's nothing left to find.
                if (firstPrompt is not null && info.Cwd is not null) break;
            }

            var title = summary ?? firstPrompt ?? "(no prompt recorded)";
            info.Title = Truncate(Collapse(title), 160);
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not parse header of {path}: {ex.Message}");
            info.Title = "(unreadable)";
        }
    }

    private static bool IsMeta(JsonElement root) =>
        root.TryGetProperty("isMeta", out var m) && m.ValueKind == JsonValueKind.True;

    /// <summary>message.content is either a plain string or an array of typed blocks.</summary>
    private static string ExtractText(JsonElement root)
    {
        if (!root.TryGetProperty("message", out var message) ||
            message.ValueKind != JsonValueKind.Object ||
            !message.TryGetProperty("content", out var content))
            return "";

        if (content.ValueKind == JsonValueKind.String)
            return content.GetString() ?? "";

        if (content.ValueKind != JsonValueKind.Array) return "";

        var parts = new List<string>();
        foreach (var block in content.EnumerateArray())
        {
            if (block.ValueKind == JsonValueKind.Object &&
                block.TryGetProperty("text", out var txt) &&
                txt.ValueKind == JsonValueKind.String)
            {
                parts.Add(txt.GetString() ?? "");
            }
        }
        return string.Join(" ", parts);
    }

    /// <summary>
    /// Rejects the machinery that shows up as "user" turns: system reminders, hook output,
    /// slash-command wrappers and the local-command stdout envelope. None of them describe
    /// what the session was about.
    /// </summary>
    private static bool IsUsableTitle(string text)
    {
        var t = text.TrimStart();
        if (t.Length == 0) return false;
        if (t.StartsWith('<')) return false;
        if (t.StartsWith("Caveat:", StringComparison.Ordinal)) return false;
        if (t.StartsWith("[Request interrupted", StringComparison.Ordinal)) return false;
        return true;
    }

    private static string Collapse(string s) =>
        string.Join(' ', s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max].TrimEnd() + "…";

    private static long DirectorySize(string dir)
    {
        try
        {
            long total = 0;
            foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                try { total += new FileInfo(f).Length; }
                catch { /* vanished mid-scan; ignore */ }
            }
            return total;
        }
        catch { return 0; }
    }
}
