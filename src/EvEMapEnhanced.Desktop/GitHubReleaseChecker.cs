using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace EvEMapEnhanced.Desktop;

public sealed class GitHubReleaseChecker
{
    private static readonly HttpClient Http = CreateHttpClient();

    public async Task<GitHubReleaseCheckResult> CheckForUpdateAsync(CancellationToken cancellationToken = default)
    {
        using var response = await Http.GetAsync(AppMetadata.GitHubLatestReleaseApiUrl, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var release = await JsonSerializer.DeserializeAsync(
            stream,
            GitHubReleaseJsonContext.Default.GitHubLatestReleaseResponse,
            cancellationToken);

        if (release is null || string.IsNullOrWhiteSpace(release.TagName))
            throw new InvalidOperationException("GitHub не вернул данные о последнем релизе.");

        string latestVersion = NormalizeVersionTag(release.TagName);
        string currentVersion = AppMetadata.CurrentVersion;
        bool updateAvailable = IsNewerVersion(latestVersion, currentVersion);

        return new GitHubReleaseCheckResult(
            currentVersion,
            latestVersion,
            updateAvailable,
            string.IsNullOrWhiteSpace(release.HtmlUrl) ? AppMetadata.GitHubReleasesUrl : release.HtmlUrl);
    }

    public static bool IsNewerVersion(string latest, string current)
    {
        if (!Version.TryParse(NormalizeVersionTag(latest), out Version? latestVersion))
            return false;
        if (!Version.TryParse(NormalizeVersionTag(current), out Version? currentVersion))
            return true;

        return latestVersion > currentVersion;
    }

    public static string NormalizeVersionTag(string tag)
    {
        string trimmed = tag.Trim();
        return trimmed.StartsWith('v') || trimmed.StartsWith('V')
            ? trimmed[1..]
            : trimmed;
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(20),
        };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("EvEMapEnhanced", AppMetadata.CurrentVersion));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

}

public sealed record GitHubReleaseCheckResult(
    string CurrentVersion,
    string LatestVersion,
    bool UpdateAvailable,
    string ReleasePageUrl);

internal sealed class GitHubLatestReleaseResponse
{
    [JsonPropertyName("tag_name")]
    public string? TagName { get; set; }

    [JsonPropertyName("html_url")]
    public string? HtmlUrl { get; set; }
}

[JsonSerializable(typeof(GitHubLatestReleaseResponse))]
internal partial class GitHubReleaseJsonContext : JsonSerializerContext;
