using System.Text;

namespace ClaudeNecromancer.Core;

/// <summary>Append-only activity log. Deliberately dumb: no rotation library, no dependencies.</summary>
public static class Log
{
    private static readonly Lock Gate = new();
    private const long MaxBytes = 2 * 1024 * 1024;

    public static event Action<string>? LineWritten;

    public static void Info(string message) => Write("INFO ", message);
    public static void Warn(string message) => Write("WARN ", message);
    public static void Error(string message) => Write("ERROR", message);

    private static void Write(string level, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {level} {message}";
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(ClaudePaths.AppDataDir);
                var path = ClaudePaths.LogPath;

                // Keep the log from growing without bound: once it passes the cap, keep the tail.
                if (File.Exists(path) && new FileInfo(path).Length > MaxBytes)
                {
                    var keep = File.ReadLines(path).TakeLast(500).ToList();
                    File.WriteAllLines(path, keep, Encoding.UTF8);
                }

                File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch
        {
            // Logging must never be the reason the app falls over.
        }

        try { LineWritten?.Invoke(line); } catch { }
    }

    public static IEnumerable<string> Tail(int lines)
    {
        try
        {
            return File.Exists(ClaudePaths.LogPath)
                ? File.ReadLines(ClaudePaths.LogPath).TakeLast(lines).ToList()
                : Enumerable.Empty<string>();
        }
        catch { return Enumerable.Empty<string>(); }
    }
}
