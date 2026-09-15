using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SmartLayoutSwitcher.App.Services;

public enum UpdateCheckStatus
{
    UpToDate,
    UpdateAvailable,
    Failed,
}

public sealed record UpdateInfo(Version Version, Uri ReleasePageUri);

public sealed record UpdateCheckResult(UpdateCheckStatus Status, UpdateInfo? Update = null);

/// <summary>
/// Reads the newest public GitHub release and verifies that it contains an
/// installer. Downloading and running it remain the browser's responsibility.
/// </summary>
public sealed class UpdateService
{
    private const string LatestReleaseUrl =
        "https://api.github.com/repos/vldpotapov/SmartLayoutSwitcher/releases/latest";

    private static readonly HttpClient HttpClient = CreateHttpClient();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public async Task<UpdateCheckResult> CheckAsync(Version installedVersion, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await HttpClient.GetAsync(LatestReleaseUrl, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return new UpdateCheckResult(UpdateCheckStatus.Failed);

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var release = await JsonSerializer.DeserializeAsync<GitHubRelease>(stream, JsonOptions, cancellationToken);
            if (release is null || !TryParseVersion(release.TagName, out var latestVersion))
                return new UpdateCheckResult(UpdateCheckStatus.Failed);

            if (latestVersion <= NormalizeVersion(installedVersion))
                return new UpdateCheckResult(UpdateCheckStatus.UpToDate);

            var asset = release.Assets?.FirstOrDefault(IsInstallerAsset);
            if (asset is null || !Uri.TryCreate(release.HtmlUrl, UriKind.Absolute, out var releasePageUri) ||
                !IsGitHubReleasePage(releasePageUri))
            {
                return new UpdateCheckResult(UpdateCheckStatus.Failed);
            }

            return new UpdateCheckResult(
                UpdateCheckStatus.UpdateAvailable,
                new UpdateInfo(latestVersion, releasePageUri));
        }
        catch (HttpRequestException)
        {
            return new UpdateCheckResult(UpdateCheckStatus.Failed);
        }
        catch (JsonException)
        {
            return new UpdateCheckResult(UpdateCheckStatus.Failed);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new UpdateCheckResult(UpdateCheckStatus.Failed);
        }
        catch (Exception)
        {
            // Update checks are optional and must never affect the switcher.
            return new UpdateCheckResult(UpdateCheckStatus.Failed);
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SmartLayoutSwitcher/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    private static bool IsInstallerAsset(GitHubAsset asset) =>
        asset.Name.StartsWith("SmartLayoutSwitcher-Setup-", StringComparison.OrdinalIgnoreCase) &&
        asset.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);

    private static bool IsGitHubReleasePage(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttps &&
        uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) &&
        uri.AbsolutePath.StartsWith("/vldpotapov/SmartLayoutSwitcher/releases/", StringComparison.OrdinalIgnoreCase);

    private static bool TryParseVersion(string? tagName, out Version version)
    {
        var value = tagName?.Trim().TrimStart('v', 'V');
        if (Version.TryParse(value, out var parsed))
        {
            version = NormalizeVersion(parsed);
            return true;
        }

        version = new Version(0, 0, 0);
        return false;
    }

    private static Version NormalizeVersion(Version version) =>
        new(version.Major, version.Minor, Math.Max(0, version.Build));

    private sealed record GitHubRelease(
        [property: JsonPropertyName("tag_name")] string TagName,
        [property: JsonPropertyName("html_url")] string HtmlUrl,
        [property: JsonPropertyName("assets")] GitHubAsset[]? Assets);

    private sealed record GitHubAsset(
        [property: JsonPropertyName("name")] string Name);
}
