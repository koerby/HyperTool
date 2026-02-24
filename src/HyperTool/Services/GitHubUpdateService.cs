using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace HyperTool.Services;

public sealed class GitHubUpdateService : IUpdateService
{
    private static readonly HttpClient HttpClient = new();

    public async Task<(bool Success, bool HasUpdate, string Message, string? LatestVersion, string? ReleaseUrl)> CheckForUpdateAsync(
        string owner,
        string repo,
        string currentVersion,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repo))
        {
            return (false, false, "GitHub Owner/Repo fehlt.", null, null);
        }

        var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{owner}/{repo}/releases/latest");
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("HyperTool", "1.0"));

        try
        {
            using var response = await HttpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return (false, false, $"GitHub API Fehler: {(int)response.StatusCode}", null, null);
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);

            var latestTag = doc.RootElement.TryGetProperty("tag_name", out var tagElement)
                ? (tagElement.GetString() ?? string.Empty).Trim()
                : string.Empty;

            var htmlUrl = doc.RootElement.TryGetProperty("html_url", out var urlElement)
                ? urlElement.GetString()
                : null;

            if (string.IsNullOrWhiteSpace(latestTag))
            {
                return (false, false, "Keine Versionsinformation im Release gefunden.", null, htmlUrl);
            }

            var latestVersion = NormalizeVersion(latestTag);
            var current = NormalizeVersion(currentVersion);

            if (!Version.TryParse(latestVersion, out var latestParsed) || !Version.TryParse(current, out var currentParsed))
            {
                var same = string.Equals(latestVersion, current, StringComparison.OrdinalIgnoreCase);
                return (true, !same, same ? "Bereits aktuell." : $"Update verfügbar: {latestTag}", latestTag, htmlUrl);
            }

            var hasUpdate = latestParsed > currentParsed;
            return (true, hasUpdate, hasUpdate ? $"Update verfügbar: {latestTag}" : "Bereits aktuell.", latestTag, htmlUrl);
        }
        catch (Exception ex)
        {
            return (false, false, $"Updatecheck fehlgeschlagen: {ex.Message}", null, null);
        }
    }

    private static string NormalizeVersion(string value)
    {
        var normalized = value.Trim();
        if (normalized.StartsWith("v", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[1..];
        }

        return normalized;
    }
}
