using Microsoft.Win32;

namespace ClaudeNecromancer.Core;

/// <summary>
/// Registers the app under HKCU Run so it starts with Windows.
///
/// HKCU, not HKLM: this needs no administrator rights and affects only the current user.
/// </summary>
public static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ClaudeNecromancer";

    public static bool IsRegistered()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is not null;
        }
        catch { return false; }
    }

    public static void Set(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
                            ?? Registry.CurrentUser.CreateSubKey(RunKey);

            if (enabled)
            {
                var exe = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exe))
                {
                    Log.Warn("Could not determine executable path; run-at-login not registered.");
                    return;
                }
                key.SetValue(ValueName, $"\"{exe}\" --minimized");
                Log.Info("Registered to run at login.");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                Log.Info("Removed run-at-login registration.");
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Could not update run-at-login: {ex.Message}");
        }
    }
}
