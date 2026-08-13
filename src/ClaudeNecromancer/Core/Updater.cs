using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ClaudeNecromancer.Core;

public enum UpdateState
{
    Idle,
    Checking,
    UpToDate,
    UpdateAvailable,
    Downloading,
    ReadyToInstall,
    Failed,
}

public sealed record ReleaseInfo
{
    public required string Version { get; init; }
    public required string Name { get; init; }
    public required string Notes { get; init; }
    public required string AssetUrl { get; init; }
    public required string AssetName { get; init; }
    public required string Sha256 { get; init; }
    public required string PageUrl { get; init; }
    public long Bytes { get; init; }
}

/// <summary>
/// Finds, fetches and installs a newer release from GitHub.
///
/// The shape is deliberately narrow. It asks GitHub what the newest release is; if that is newer
/// than this build it can download the executable attached to it, check that what arrived is what
/// the release says it should be, and swap it in. It never runs anything it has not checked.
///
/// Why the checking is not optional
/// --------------------------------
/// Downloading an executable and running it hands whatever served that file the ability to run code
/// as the person using this program. Over HTTPS with a valid certificate that is GitHub, which is
/// the intent — but a release also has to survive being wrong: a truncated download, a proxy that
/// returns an error page with a 200, an asset replaced after the notes were written. So every
/// release publishes the SHA-256 of its executable, and a download whose hash does not match is
/// deleted and reported rather than run. scripts/make-release.ps1 writes that hash into the release
/// notes, so the two cannot drift apart by hand.
/// </summary>
public sealed class Updater
{
    private const string ApiRoot = "https://api.github.com";

    private readonly HttpClient _http;

    public UpdateState State { get; private set; } = UpdateState.Idle;
    public ReleaseInfo? Latest { get; private set; }
    public string? Error { get; private set; }
    public double Progress { get; private set; }

    /// <summary>Raised on every state or progress movement, on a background thread.</summary>
    public event Action? Changed;

    private string? _downloadedPath;

    public Updater()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        // GitHub rejects requests without a User-Agent outright.
        _http.DefaultRequestHeaders.Add("User-Agent", "ClaudeNecromancer/" + VersionInfo.Plain());
        _http.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");
    }

    private void Set(UpdateState state, string? error = null)
    {
        State = state;
        Error = error;
        if (error is not null) Log.Warn($"Updater: {error}");
        Changed?.Invoke();
    }

    /// <summary>Asks GitHub for the newest release and compares it with this build.</summary>
    public async Task CheckAsync(CancellationToken ct = default)
    {
        Set(UpdateState.Checking);
        try
        {
            var url = $"{ApiRoot}/repos/{VersionInfo.UpdateRepository}/releases/latest";
            using var response = await _http.GetAsync(url, ct);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // No releases published yet is a normal state, not a fault.
                Set(UpdateState.UpToDate);
                return;
            }

            response.EnsureSuccessStatusCode();
            var root = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct)).RootElement;

            var tag = root.GetProperty("tag_name").GetString() ?? "";
            if (!VersionInfo.IsNewerThanThis(tag))
            {
                Set(UpdateState.UpToDate);
                return;
            }

            var notes = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";

            // Only ever offer a Windows executable, and only the one this release actually names.
            var asset = root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array
                ? assets.EnumerateArray().FirstOrDefault(a =>
                    (a.GetProperty("name").GetString() ?? "").EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                : default;

            if (asset.ValueKind != JsonValueKind.Object)
            {
                Set(UpdateState.Failed, $"Release {tag} has no .exe attached to it.");
                return;
            }

            var assetName = asset.GetProperty("name").GetString()!;
            var sha = FindSha256(notes, assetName);

            if (sha is null)
            {
                // Refusing here is the point: an unverifiable download is not offered at all.
                Set(UpdateState.Failed,
                    $"Release {tag} does not publish a SHA-256 for {assetName}, so it cannot be " +
                    "verified and will not be installed. Update manually from the releases page.");
                return;
            }

            Latest = new ReleaseInfo
            {
                Version = tag,
                Name = root.TryGetProperty("name", out var n) ? n.GetString() ?? tag : tag,
                Notes = notes,
                AssetUrl = asset.GetProperty("browser_download_url").GetString()!,
                AssetName = assetName,
                Sha256 = sha,
                PageUrl = root.GetProperty("html_url").GetString()!,
                Bytes = asset.TryGetProperty("size", out var s) ? s.GetInt64() : 0,
            };

            Log.Info($"Update available: {tag} ({assetName}, {Latest.Bytes / 1024:N0} KB)");
            Set(UpdateState.UpdateAvailable);
        }
        catch (OperationCanceledException)
        {
            Set(UpdateState.Idle);
        }
        catch (Exception ex)
        {
            Set(UpdateState.Failed, $"Could not check for updates: {ex.Message}");
        }
    }

    /// <summary>Downloads the release executable and verifies its hash before accepting it.</summary>
    public async Task DownloadAsync(CancellationToken ct = default)
    {
        if (Latest is null) return;

        Set(UpdateState.Downloading);
        Progress = 0;

        var dir = Path.Combine(Path.GetTempPath(), "ClaudeNecromancer", "update");
        Directory.CreateDirectory(dir);
        var target = Path.Combine(dir, Latest.AssetName);

        try
        {
            using (var response = await _http.GetAsync(
                       Latest.AssetUrl, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                response.EnsureSuccessStatusCode();
                var total = response.Content.Headers.ContentLength ?? Latest.Bytes;

                await using var input = await response.Content.ReadAsStreamAsync(ct);
                await using var output = File.Create(target);

                var buffer = new byte[81920];
                long done = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, ct)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), ct);
                    done += read;
                    if (total > 0)
                    {
                        Progress = (double)done / total;
                        Changed?.Invoke();
                    }
                }
            }

            var actual = HashOf(target);
            if (!actual.Equals(Latest.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                TryDelete(target);
                Set(UpdateState.Failed,
                    "The downloaded file does not match the checksum published for this release. " +
                    "It has been deleted and will not be run.\n\n" +
                    $"Expected: {Latest.Sha256}\nActual:   {actual}");
                return;
            }

            _downloadedPath = target;
            Progress = 1;
            Log.Info($"Update {Latest.Version} downloaded and verified.");
            Set(UpdateState.ReadyToInstall);
        }
        catch (OperationCanceledException)
        {
            TryDelete(target);
            Set(UpdateState.UpdateAvailable);
        }
        catch (Exception ex)
        {
            TryDelete(target);
            Set(UpdateState.Failed, $"Download failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Swaps the verified executable in and restarts.
    ///
    /// A running .exe is locked by Windows, so it cannot replace itself directly. Instead a small
    /// PowerShell script waits for this process to exit, copies the new file over the old one and
    /// starts it again. The old executable is kept alongside as .bak until the next update, so a
    /// swap that goes wrong leaves something to go back to.
    /// </summary>
    public bool InstallAndRestart()
    {
        if (State != UpdateState.ReadyToInstall || _downloadedPath is null) return false;
        if (!File.Exists(_downloadedPath)) return false;

        var current = Environment.ProcessPath;
        if (string.IsNullOrEmpty(current))
        {
            Set(UpdateState.Failed, "Could not determine this application's own path.");
            return false;
        }

        var script = Path.Combine(Path.GetTempPath(), "ClaudeNecromancer", "apply-update.ps1");
        var pid = Environment.ProcessId;

        // $$ raw string: single braces stay literal (PowerShell is full of them),
        // and {{ }} marks the handful of values interpolated from C#.
        var ps = $$"""
            $ErrorActionPreference = 'Stop'
            try { Wait-Process -Id {{pid}} -Timeout 120 -ErrorAction SilentlyContinue } catch { }
            Start-Sleep -Milliseconds 500
            $target = {{Quote(current)}}
            $source = {{Quote(_downloadedPath)}}
            $backup = "$target.bak"
            try {
                if (Test-Path $backup) { Remove-Item $backup -Force -ErrorAction SilentlyContinue }
                if (Test-Path $target) { Move-Item $target $backup -Force }
                Copy-Item $source $target -Force
                Start-Process $target
            } catch {
                # Put the old build back rather than leaving nothing runnable.
                if ((Test-Path $backup) -and -not (Test-Path $target)) { Move-Item $backup $target -Force }
                Start-Process $target
            }
            """;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(script)!);
            File.WriteAllText(script, ps, Encoding.UTF8);

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{script}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
            });

            Log.Info($"Applying update {Latest?.Version}; restarting.");
            return true;
        }
        catch (Exception ex)
        {
            Set(UpdateState.Failed, $"Could not launch the updater: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Pulls the SHA-256 out of a release body. Looked for as a 64-character hex run on a line that
    /// also mentions the asset, so prose around it does not matter.
    /// </summary>
    internal static string? FindSha256(string body, string assetName)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;

        string? Hex(string line)
        {
            foreach (var token in line.Split(new[] { ' ', '\t', '`', '*', '|', ':', '(', ')' },
                         StringSplitOptions.RemoveEmptyEntries))
            {
                if (token.Length == 64 && token.All(Uri.IsHexDigit)) return token.ToLowerInvariant();
            }
            return null;
        }

        var lines = body.Split('\n');

        // Prefer a hash on the same line as the asset name — releases may list several files.
        foreach (var line in lines)
        {
            if (line.Contains(assetName, StringComparison.OrdinalIgnoreCase) && Hex(line) is { } h)
                return h;
        }

        // Otherwise accept a lone hash anywhere in the notes, but only if there is exactly one:
        // ambiguity here would mean guessing which file it belongs to.
        var found = lines.Select(Hex).Where(h => h is not null).Distinct().ToList();
        return found.Count == 1 ? found[0] : null;
    }

    internal static string HashOf(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string Quote(string path) => "'" + path.Replace("'", "''") + "'";

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
