namespace ClaudeNecromancer.Core;

/// <summary>
/// Every location Claude Code's retention sweep touches, plus the ones it deliberately spares.
///
/// Source: https://code.claude.com/docs/en/claude-directory ("Cleaned up automatically").
/// The sweep runs at startup and deletes anything older than cleanupPeriodDays (default 30).
/// </summary>
public static class ClaudePaths
{
    /// <summary>Claude Code's default retention window, in days, when settings.json says nothing.</summary>
    public const int DefaultCleanupPeriodDays = 30;

    public static string Home =>
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>~/.claude</summary>
    public static string ClaudeDir => Path.Combine(Home, ".claude");

    /// <summary>~/.claude/projects — one folder per project, holding the session transcripts.</summary>
    public static string ProjectsDir => Path.Combine(ClaudeDir, "projects");

    /// <summary>~/.claude/settings.json — where cleanupPeriodDays lives.</summary>
    public static string SettingsJson => Path.Combine(ClaudeDir, "settings.json");

    /// <summary>
    /// ~/.claude/file-history/&lt;session&gt; — pre-edit file snapshots backing checkpoint restore.
    /// Swept on its own path, NOT with the parent transcript, so it needs touching separately.
    /// </summary>
    public static string FileHistoryDir => Path.Combine(ClaudeDir, "file-history");

    /// <summary>Our own state, kept well away from ~/.claude so the sweep can never reach it.</summary>
    public static string AppDataDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClaudeNecromancer");

    public static string ConfigPath => Path.Combine(AppDataDir, "config.json");
    public static string LogPath => Path.Combine(AppDataDir, "necromancer.log");

    public static string DefaultArchiveDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ClaudeNecromancer", "Archive");

    public static string DefaultChatBackupDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ClaudeNecromancer", "ChatBackup");

    /// <summary>
    /// Claude Code encodes the project's working directory into the folder name by replacing
    /// path separators and colons with dashes: "F:\Claude Necromancer" -> "F--Claude-Necro".
    /// We can't invert that unambiguously, so we recover the real cwd from the transcript
    /// itself where possible and fall back to a tidied-up version of the folder name.
    /// </summary>
    public static string PrettifyProjectFolder(string folderName)
    {
        if (string.IsNullOrWhiteSpace(folderName)) return folderName;

        var s = folderName;

        // A leading "X--" is a drive letter.
        if (s.Length > 3 && char.IsLetter(s[0]) && s[1] == '-' && s[2] == '-')
            s = s[0] + ":\\" + s[3..];

        return s.Replace('-', ' ').Replace(":\\ ", ":\\");
    }
}
