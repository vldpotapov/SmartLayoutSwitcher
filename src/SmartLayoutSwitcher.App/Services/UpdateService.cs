using System.IO;
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

public sealed record UpdateInfo(Version Version, Uri DownloadUri, string FileName);

public sealed record UpdateCheckResult(UpdateCheckStatus Status, UpdateInfo? Update = null);

/// <summary>
/// Reads the newest public GitHub release and downloads its installer on demand.
/// No GitHub credential is required to update the public application.
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
            if (asset is null || !Uri.TryCreate(asset.BrowserDownloadUrl, UriKind.Absolute, out var downloadUri) ||
                !IsGitHubDownload(downloadUri))
            {
                return new UpdateCheckResult(UpdateCheckStatus.Failed);
            }

            return new UpdateCheckResult(
                UpdateCheckStatus.UpdateAvailable,
                new UpdateInfo(latestVersion, downloadUri, Path.GetFileName(asset.Name)));
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

    public async Task<string> DownloadInstallerAsync(UpdateInfo update, CancellationToken cancellationToken = default)
    {
        var downloadsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads");
        Directory.CreateDirectory(downloadsDirectory);

        var destinationPath = Path.Combine(downloadsDirectory, update.FileName);
        var temporaryPath = destinationPath + ".download";

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, update.DownloadUri);
            using var response = await HttpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var destination = new FileStream(
                temporaryPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                useAsync: true);
            await source.CopyToAsync(destination, cancellationToken);

            File.Move(temporaryPath, destinationPath, overwrite: true);
            return destinationPath;
        }
        catch
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch
            {
                // A failed cleanup must not hide the download error.
            }

            throw;
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

    private static bool IsGitHubDownload(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttps &&
        (uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
         uri.Host.EndsWith(".github.com", StringComparison.OrdinalIgnoreCase));

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
        [property: JsonPropertyName("assets")] GitHubAsset[]? Assets);

    private sealed record GitHubAsset(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("browser_download_url")] string BrowserDownloadUrl);
}
