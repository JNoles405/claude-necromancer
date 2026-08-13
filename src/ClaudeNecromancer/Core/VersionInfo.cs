using System.Reflection;

namespace ClaudeNecromancer.Core;

/// <summary>
/// The application's version, and the rules for comparing it with a release tag.
///
/// The numbers come from &lt;Version&gt; in ClaudeNecromancer.csproj, which is the single place the
/// version is written. scripts/make-release.ps1 reads the same property, so the built binary, the
/// git tag and the installer filename cannot drift apart by hand.
/// </summary>
public static class VersionInfo
{
    /// <summary>owner/repo whose GitHub releases are offered as updates.</summary>
    public const string UpdateRepository = "JNoles405/claude-necromancer";

    private static readonly Version Assembly_ =
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

    public static int Major => Assembly_.Major;
    public static int Minor => Assembly_.Minor;
    public static int Patch => Assembly_.Build < 0 ? 0 : Assembly_.Build;

    /// <summary>
    /// How a version is written on screen: the major number as it is, the other two padded to two
    /// digits. 1.0.0 reads "v1.00.00"; 2.11.3 reads "v2.11.03".
    ///
    /// Padded because a release number is a label, not a quantity — a column of them that all have
    /// the same shape is easier to compare at a glance than one where v1.9.0 and v1.10.0 are
    /// different widths.
    /// </summary>
    public static string Display(int major, int minor, int patch) =>
        $"v{major}.{minor:00}.{patch:00}";

    public static string Display() => Display(Major, Minor, Patch);

    /// <summary>The plain form used by git tags and asset filenames: "1.0.0".</summary>
    public static string Plain() => $"{Major}.{Minor}.{Patch}";

    /// <summary>
    /// Reads "1.2.3", "v1.2.3" or "v1.02.03" into three numbers.
    ///
    /// Tolerant on purpose: a release tag is typed by a person, and a release called "v1.02.00"
    /// must not read as older than "1.2.0" when they are the same thing. Returns false for anything
    /// it cannot make numbers out of, so an unparseable tag is ignored rather than treated as
    /// version zero — which would offer every release as an update.
    /// </summary>
    public static bool TryParse(string? text, out int major, out int minor, out int patch)
    {
        major = minor = patch = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var trimmed = text.Trim();
        if (trimmed.StartsWith('v') || trimmed.StartsWith('V')) trimmed = trimmed[1..];

        var parts = trimmed.Split('.', StringSplitOptions.TrimEntries);
        if (parts.Length < 2) return false;

        var values = new int[3];
        for (var i = 0; i < Math.Min(3, parts.Length); i++)
        {
            if (parts[i].Length == 0 || !parts[i].All(char.IsAsciiDigit)) return false;
            if (!int.TryParse(parts[i], out values[i])) return false;
        }

        major = values[0];
        minor = values[1];
        patch = parts.Length > 2 ? values[2] : 0;
        return true;
    }

    /// <summary>True when <paramref name="text"/> names a version newer than this build.</summary>
    public static bool IsNewerThanThis(string? text)
    {
        if (!TryParse(text, out var major, out var minor, out var patch)) return false;

        if (major != Major) return major > Major;
        if (minor != Minor) return minor > Minor;
        return patch > Patch;
    }
}
