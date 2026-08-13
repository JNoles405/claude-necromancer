using System.Net;
using System.Text;
using System.Text.Json;

namespace ClaudeNecromancer.Core;

public sealed record ChatBackupResult(int Conversations, int Written, string Destination, string? Error)
{
    public bool Success => Error is null;
}

/// <summary>
/// Backs up claude.ai conversations to local disk.
///
/// Why this exists, and why it is a backup rather than a "touch": claude.ai conversations are held
/// server-side and are NOT deleted for inactivity — they persist until you delete them. So there is
/// nothing on the Chat side that keeping-alive would help with. The real exposure is different:
/// accidental deletion, account trouble, or the reports of history going missing. A local copy is
/// the honest answer to that, and touching would not have helped at all.
///
/// IMPORTANT CAVEAT: this uses the same private endpoints the claude.ai web app calls, authenticated
/// with the user's own session cookie. There is no public conversations API. These endpoints are
/// undocumented and Anthropic can change them without notice, at which point this stops working and
/// will need updating. It reads only; it never writes to the account.
/// </summary>
public static class ChatBackup
{
    private const string Origin = "https://claude.ai";

    /// <summary>Chrome-like UA. The endpoints sit behind Cloudflare, which rejects default .NET agents.</summary>
    private const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) " +
        "Chrome/131.0.0.0 Safari/537.36";

    public static async Task<ChatBackupResult> RunAsync(
        string sessionKey, string destRoot, IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sessionKey))
            return new ChatBackupResult(0, 0, destRoot, "No session key configured.");

        var handler = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(90) };

        http.DefaultRequestHeaders.Add("User-Agent", UserAgent);
        http.DefaultRequestHeaders.Add("Accept", "application/json");
        http.DefaultRequestHeaders.Add("Referer", Origin + "/chats");
        http.DefaultRequestHeaders.Add("Cookie", "sessionKey=" + sessionKey.Trim());

        try
        {
            progress?.Report("Identifying account…");
            var orgs = await GetJsonAsync(http, $"{Origin}/api/organizations", ct);

            if (orgs.ValueKind != JsonValueKind.Array || orgs.GetArrayLength() == 0)
                return new ChatBackupResult(0, 0, destRoot,
                    "No organizations returned. The session key is probably expired — grab a fresh one.");

            var total = 0;
            var written = 0;
            var stamp = DateTime.Now.ToString("yyyy-MM-dd");

            foreach (var org in orgs.EnumerateArray())
            {
                ct.ThrowIfCancellationRequested();

                var orgId = org.GetProperty("uuid").GetString()!;
                var orgName = org.TryGetProperty("name", out var n) ? n.GetString() : orgId;

                progress?.Report($"Listing conversations for {orgName}…");
                var list = await GetJsonAsync(
                    http, $"{Origin}/api/organizations/{orgId}/chat_conversations", ct);

                if (list.ValueKind != JsonValueKind.Array) continue;

                var destDir = Path.Combine(destRoot, stamp, Sanitize(orgName ?? orgId));
                Directory.CreateDirectory(destDir);

                foreach (var convo in list.EnumerateArray())
                {
                    ct.ThrowIfCancellationRequested();
                    total++;

                    var uuid = convo.GetProperty("uuid").GetString()!;
                    var name = convo.TryGetProperty("name", out var cn) ? cn.GetString() : null;
                    var label = string.IsNullOrWhiteSpace(name) ? uuid[..8] : name!;

                    progress?.Report($"[{total}] {Truncate(label, 60)}");

                    try
                    {
                        var full = await GetJsonAsync(http,
                            $"{Origin}/api/organizations/{orgId}/chat_conversations/{uuid}" +
                            "?tree=True&rendering_mode=messages", ct);

                        var baseName = Sanitize($"{Truncate(label, 70)}_{uuid[..8]}");
                        await File.WriteAllTextAsync(
                            Path.Combine(destDir, baseName + ".json"),
                            JsonSerializer.Serialize(full,
                                new JsonSerializerOptions { WriteIndented = true }),
                            ct);

                        await File.WriteAllTextAsync(
                            Path.Combine(destDir, baseName + ".md"),
                            RenderMarkdown(full, label), Encoding.UTF8, ct);

                        written++;
                    }
                    catch (Exception ex)
                    {
                        Log.Warn($"Chat backup: could not fetch {uuid}: {ex.Message}");
                    }

                    // Deliberately unhurried — this is someone's own account, not a scraping target.
                    await Task.Delay(250, ct);
                }
            }

            Log.Info($"Chat backup complete: {written}/{total} conversations -> {destRoot}");
            return new ChatBackupResult(total, written, destRoot, null);
        }
        catch (OperationCanceledException)
        {
            return new ChatBackupResult(0, 0, destRoot, "Cancelled.");
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized ||
                                              ex.StatusCode == HttpStatusCode.Forbidden)
        {
            return new ChatBackupResult(0, 0, destRoot,
                "claude.ai rejected the session key (401/403). It has probably expired — copy a fresh one.");
        }
        catch (Exception ex)
        {
            Log.Error($"Chat backup failed: {ex}");
            return new ChatBackupResult(0, 0, destRoot, ex.Message);
        }
    }

    private static async Task<JsonElement> GetJsonAsync(HttpClient http, string url, CancellationToken ct)
    {
        using var response = await http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        var text = await response.Content.ReadAsStringAsync(ct);
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    /// <summary>Renders a conversation as readable Markdown alongside the raw JSON.</summary>
    private static string RenderMarkdown(JsonElement convo, string title)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {title}").AppendLine();

        if (convo.TryGetProperty("created_at", out var created))
            sb.AppendLine($"- Created: {created.GetString()}");
        if (convo.TryGetProperty("updated_at", out var updated))
            sb.AppendLine($"- Updated: {updated.GetString()}");
        if (convo.TryGetProperty("uuid", out var uuid))
            sb.AppendLine($"- UUID: `{uuid.GetString()}`");

        sb.AppendLine().AppendLine("---").AppendLine();

        if (!convo.TryGetProperty("chat_messages", out var messages) ||
            messages.ValueKind != JsonValueKind.Array)
        {
            sb.AppendLine("_No messages found in this export._");
            return sb.ToString();
        }

        foreach (var message in messages.EnumerateArray())
        {
            var sender = message.TryGetProperty("sender", out var s) ? s.GetString() : "?";
            var who = sender == "human" ? "You" : "Claude";
            sb.AppendLine($"## {who}").AppendLine();
            sb.AppendLine(ExtractMessageText(message)).AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>
    /// Prefers the structured content blocks and falls back to the legacy flat "text" field,
    /// which older conversations still use.
    /// </summary>
    private static string ExtractMessageText(JsonElement message)
    {
        if (message.TryGetProperty("content", out var content) &&
            content.ValueKind == JsonValueKind.Array)
        {
            var parts = new List<string>();
            foreach (var block in content.EnumerateArray())
            {
                if (block.ValueKind != JsonValueKind.Object) continue;
                if (block.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String)
                    parts.Add(t.GetString() ?? "");
            }
            if (parts.Count > 0) return string.Join("\n\n", parts);
        }

        return message.TryGetProperty("text", out var flat) && flat.ValueKind == JsonValueKind.String
            ? flat.GetString() ?? ""
            : "";
    }

    private static string Sanitize(string name)
    {
        var cleaned = new string(name
            .Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)
            .ToArray())
            .Trim().Trim('.');
        return string.IsNullOrWhiteSpace(cleaned) ? "untitled" : cleaned;
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}
