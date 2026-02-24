using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace HyperTool.Services;

public sealed class GitHubUpdateService : IUpdateService
{
    private static readonly HttpClient HttpClient = new();

    static GitHubUpdateService()
    {
        HttpClient.DefaultRequestHeaders.UserAgent.Clear();
        HttpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("HyperTool", "1.0"));
        HttpClient.DefaultRequestHeaders.Accept.Clear();
        HttpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

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

        var repoUrl = $"https://github.com/{owner}/{repo}";
        var releasePageUrl = $"{repoUrl}/releases";
        var latestReleaseApi = $"https://api.github.com/repos/{owner}/{repo}/releases/latest";
        var tagsApi = $"https://api.github.com/repos/{owner}/{repo}/tags?per_page=1";

        try
        {
            using var releaseResponse = await HttpClient.GetAsync(latestReleaseApi, cancellationToken);
            if (releaseResponse.IsSuccessStatusCode)
            {
                var json = await releaseResponse.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(json);

                var latestTag = doc.RootElement.TryGetProperty("tag_name", out var tagElement)
                    ? (tagElement.GetString() ?? string.Empty).Trim()
                    : string.Empty;

                var htmlUrl = doc.RootElement.TryGetProperty("html_url", out var urlElement)
                    ? urlElement.GetString()
                    : releasePageUrl;

                if (string.IsNullOrWhiteSpace(latestTag))
                {
                    return (false, false, "Keine Versionsinformation im neuesten Release gefunden.", null, htmlUrl);
                }

                return CompareVersions(currentVersion, latestTag, htmlUrl);
            }

            if (releaseResponse.StatusCode != System.Net.HttpStatusCode.NotFound)
            {
                var errorMessage = await TryReadGitHubErrorMessageAsync(releaseResponse, cancellationToken);
                return (false, false, $"GitHub API Fehler: {(int)releaseResponse.StatusCode} {errorMessage}", null, releasePageUrl);
            }

            using var tagsResponse = await HttpClient.GetAsync(tagsApi, cancellationToken);
            if (!tagsResponse.IsSuccessStatusCode)
            {
                var tagsErrorMessage = await TryReadGitHubErrorMessageAsync(tagsResponse, cancellationToken);
                return (false, false, $"GitHub API Fehler: {(int)tagsResponse.StatusCode} {tagsErrorMessage}", null, releasePageUrl);
            }

            var tagsJson = await tagsResponse.Content.ReadAsStringAsync(cancellationToken);
            using var tagsDoc = JsonDocument.Parse(tagsJson);

            if (tagsDoc.RootElement.ValueKind != JsonValueKind.Array || tagsDoc.RootElement.GetArrayLength() == 0)
            {
                return (true, false, "Keine Releases oder Tags gefunden.", null, releasePageUrl);
            }

            var firstTag = tagsDoc.RootElement[0];
            var tagName = firstTag.TryGetProperty("name", out var nameElement)
                ? (nameElement.GetString() ?? string.Empty).Trim()
                : string.Empty;

            if (string.IsNullOrWhiteSpace(tagName))
            {
                return (false, false, "Tag-Information ohne Versionsnamen gefunden.", null, releasePageUrl);
            }

            var result = CompareVersions(currentVersion, tagName, $"{repoUrl}/tags");
            var prefix = result.HasUpdate ? "Update verfügbar (Tag): " : "Kein neuer Release gefunden, letzter Tag: ";
            return (result.Success, result.HasUpdate, prefix + tagName, tagName, result.ReleaseUrl);
        }
        catch (Exception ex)
        {
            return (false, false, $"Updatecheck fehlgeschlagen: {ex.Message}", null, releasePageUrl);
        }
    }

    private static (bool Success, bool HasUpdate, string Message, string? LatestVersion, string? ReleaseUrl) CompareVersions(
        string currentVersion,
        string latestTag,
        string? releaseUrl)
    {
        var latestVersion = NormalizeVersion(latestTag);
        var current = NormalizeVersion(currentVersion);

        if (!Version.TryParse(latestVersion, out var latestParsed) || !Version.TryParse(current, out var currentParsed))
        {
            var same = string.Equals(latestVersion, current, StringComparison.OrdinalIgnoreCase);
            return (true, !same, same ? "Bereits aktuell." : $"Update verfügbar: {latestTag}", latestTag, releaseUrl);
        }

        var hasUpdate = latestParsed > currentParsed;
        return (true, hasUpdate, hasUpdate ? $"Update verfügbar: {latestTag}" : "Bereits aktuell.", latestTag, releaseUrl);
    }

    private static async Task<string> TryReadGitHubErrorMessageAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(content))
            {
                return string.Empty;
            }

            using var doc = JsonDocument.Parse(content);
            if (doc.RootElement.TryGetProperty("message", out var messageElement))
            {
                var message = messageElement.GetString();
                return string.IsNullOrWhiteSpace(message) ? string.Empty : $"- {message}";
            }
        }
        catch
        {
        }

        return string.Empty;
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
