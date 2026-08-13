using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClaudeNecromancer.Core;

public enum ProtectionMode
{
    /// <summary>Touch every session found. Safest default: nothing silently ages out.</summary>
    All,
    /// <summary>Touch only the sessions ticked in the list.</summary>
    Selected,
}

public sealed class AppConfig
{
    public ProtectionMode Mode { get; set; } = ProtectionMode.All;

    /// <summary>Session GUIDs to keep alive when Mode is Selected.</summary>
    public HashSet<string> SelectedSessionIds { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>How often the scheduler fires, in hours.</summary>
    public double IntervalHours { get; set; } = 12;

    public bool ScheduleEnabled { get; set; } = true;

    /// <summary>
    /// Touch once at launch as well. This is the safety net for a machine that was switched off:
    /// a timer alone can't fire while Windows isn't running.
    /// </summary>
    public bool TouchOnStartup { get; set; } = true;

    public bool TouchSidecars { get; set; } = true;
    public bool TouchFileHistory { get; set; } = true;

    public bool ArchiveEnabled { get; set; }
    public string ArchiveDir { get; set; } = ClaudePaths.DefaultArchiveDir;

    public bool ChatBackupEnabled { get; set; }
    public string ChatBackupDir { get; set; } = ClaudePaths.DefaultChatBackupDir;

    /// <summary>claude.ai session cookie, DPAPI-encrypted for the current Windows user.</summary>
    public string? ProtectedSessionKey { get; set; }

    public bool RunAtLogin { get; set; }
    public bool StartMinimized { get; set; }
    public bool ShowNotifications { get; set; } = true;

    /// <summary>
    /// Look for a new release at launch. On by default because it was asked for, but note that it
    /// does mean the app contacts GitHub each time it starts.
    /// </summary>
    public bool CheckForUpdatesOnStartup { get; set; } = true;

    /// <summary>
    /// Download and install a found update without asking. Off by default and deliberately so:
    /// installing replaces the running executable, which is not something to do behind someone's
    /// back. The checksum is verified either way.
    /// </summary>
    public bool AutoInstallUpdates { get; set; }

    public DateTime? LastUpdateCheckUtc { get; set; }

    public DateTime? LastRunUtc { get; set; }

    [JsonIgnore]
    public TimeSpan Interval => TimeSpan.FromHours(Math.Clamp(IntervalHours, 0.25, 24 * 30));

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(ClaudePaths.ConfigPath))
            {
                var cfg = JsonSerializer.Deserialize<AppConfig>(
                    File.ReadAllText(ClaudePaths.ConfigPath), Options);
                if (cfg is not null) return cfg;
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Config unreadable, falling back to defaults: {ex.Message}");
        }
        return new AppConfig();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(ClaudePaths.AppDataDir);
            var tmp = ClaudePaths.ConfigPath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, Options));
            File.Move(tmp, ClaudePaths.ConfigPath, overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Error($"Could not save config: {ex.Message}");
        }
    }

    /// <summary>Filters a scan down to the sessions this config says to protect.</summary>
    public List<SessionInfo> Filter(IEnumerable<SessionInfo> sessions) =>
        Mode == ProtectionMode.All
            ? sessions.ToList()
            : sessions.Where(s => SelectedSessionIds.Contains(s.SessionId)).ToList();
}
