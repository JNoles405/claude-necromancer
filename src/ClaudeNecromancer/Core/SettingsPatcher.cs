using System.Text.Json;
using System.Text.Json.Nodes;

namespace ClaudeNecromancer.Core;

/// <summary>
/// Reads and edits cleanupPeriodDays in ~/.claude/settings.json.
///
/// Touching files fights the symptom; this is the root-cause fix. Raising the retention window
/// stops the sweep from targeting old sessions at all.
/// </summary>
public static class SettingsPatcher
{
    private const string Key = "cleanupPeriodDays";

    /// <summary>
    /// The retention window currently in force. Returns the documented default of 30 when the
    /// setting is absent. Managed/enterprise settings can override this and we cannot see them,
    /// which is why the UI labels this as the user-level value.
    /// </summary>
    public static int GetEffectiveCleanupPeriodDays(out bool explicitlySet, out string? problem)
    {
        explicitlySet = false;
        problem = null;

        var path = ClaudePaths.SettingsJson;
        if (!File.Exists(path)) return ClaudePaths.DefaultCleanupPeriodDays;

        try
        {
            var node = JsonNode.Parse(
                File.ReadAllText(path),
                nodeOptions: null,
                documentOptions: new JsonDocumentOptions
                {
                    CommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true,
                });

            if (node is JsonObject obj &&
                obj.TryGetPropertyValue(Key, out var value) &&
                value is not null &&
                int.TryParse(value.ToString(), out var days))
            {
                explicitlySet = true;
                return days;
            }
        }
        catch (Exception ex)
        {
            // A settings file that won't parse is worth surfacing loudly: per Anthropic's docs,
            // Claude Code PAUSES the sweep when it can't determine the retention period. Sessions
            // are safe for now, but the user's settings are broken and /status will complain.
            problem = $"settings.json could not be parsed ({ex.Message}). " +
                      "Claude Code pauses the cleanup sweep in this state.";
            Log.Warn(problem);
        }

        return ClaudePaths.DefaultCleanupPeriodDays;
    }

    /// <summary>
    /// Writes cleanupPeriodDays, preserving every other setting and the user's formatting intent.
    /// A timestamped backup is taken first — we are editing a file we do not own.
    /// </summary>
    public static void SetCleanupPeriodDays(int days)
    {
        if (days < 1) throw new ArgumentOutOfRangeException(nameof(days),
            "Claude Code rejects cleanupPeriodDays below 1; 0 fails validation.");

        var path = ClaudePaths.SettingsJson;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        JsonObject root;
        if (File.Exists(path))
        {
            var text = File.ReadAllText(path);

            var backup = Path.Combine(
                ClaudePaths.AppDataDir,
                $"settings.backup.{DateTime.Now:yyyyMMdd-HHmmss}.json");
            Directory.CreateDirectory(ClaudePaths.AppDataDir);
            File.WriteAllText(backup, text);
            Log.Info($"Backed up settings.json to {backup}");

            root = JsonNode.Parse(text, nodeOptions: null, documentOptions: new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            }) as JsonObject ?? new JsonObject();
        }
        else
        {
            root = new JsonObject();
        }

        root[Key] = days;

        // Write via a temp file so an interrupted write can't leave settings.json truncated —
        // that would pause Claude Code's sweep and trip a /status warning.
        var tmp = path + ".necromancer.tmp";
        File.WriteAllText(tmp, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, path, overwrite: true);

        Log.Info($"Set {Key} = {days} in {path}");
    }
}
