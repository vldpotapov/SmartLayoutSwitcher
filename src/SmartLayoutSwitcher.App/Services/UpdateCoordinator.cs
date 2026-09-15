using SmartLayoutSwitcher.App.Settings;

namespace SmartLayoutSwitcher.App.Services;

/// <summary>
/// Owns the persisted update state. A normal launch uses a successful cached
/// response for a day; failed requests are retried after one hour.
/// </summary>
public sealed class UpdateCoordinator
{
    private static readonly TimeSpan SuccessfulCheckInterval = TimeSpan.FromHours(24);
    private static readonly TimeSpan FailedCheckInterval = TimeSpan.FromHours(1);

    private readonly AppSettings _settings;
    private readonly UpdateService _updateService;
    private readonly Version _installedVersion;
    private readonly SemaphoreSlim _checkLock = new(1, 1);

    public UpdateCoordinator(AppSettings settings, Version installedVersion, UpdateService? updateService = null)
    {
        _settings = settings;
        _installedVersion = NormalizeVersion(installedVersion);
        _updateService = updateService ?? new UpdateService();
        AvailableUpdate = ReadCachedUpdate();

        if (AvailableUpdate is not null && AvailableUpdate.Version <= _installedVersion)
        {
            ClearAvailableUpdate();
            _settings.Save();
        }
    }

    public event Action<UpdateInfo?>? UpdateAvailabilityChanged;

    public UpdateInfo? AvailableUpdate { get; private set; }

    public bool LastCheckFailed => _settings.UpdateCheckFailed;

    public bool IsUsingFreshCache =>
        _settings.LastUpdateCheckUtc is { } checkedAt &&
        DateTimeOffset.UtcNow - checkedAt < (_settings.UpdateCheckFailed ? FailedCheckInterval : SuccessfulCheckInterval);

    public Task<string> DownloadInstallerAsync(UpdateInfo update, CancellationToken cancellationToken = default) =>
        _updateService.DownloadInstallerAsync(update, cancellationToken);

    public async Task<UpdateCheckResult> CheckAsync(bool force = false, CancellationToken cancellationToken = default)
    {
        await _checkLock.WaitAsync(cancellationToken);
        try
        {
            if (!force && IsUsingFreshCache)
                return AvailableUpdate is null
                    ? new UpdateCheckResult(UpdateCheckStatus.UpToDate)
                    : new UpdateCheckResult(UpdateCheckStatus.UpdateAvailable, AvailableUpdate);

            var result = await _updateService.CheckAsync(_installedVersion, cancellationToken);
            _settings.LastUpdateCheckUtc = DateTimeOffset.UtcNow;
            _settings.UpdateCheckFailed = result.Status == UpdateCheckStatus.Failed;

            if (result.Status == UpdateCheckStatus.UpdateAvailable && result.Update is not null)
                SetAvailableUpdate(result.Update);
            else if (result.Status == UpdateCheckStatus.UpToDate)
                ClearAvailableUpdate();

            _settings.Save();
            return result;
        }
        finally
        {
            _checkLock.Release();
        }
    }

    private UpdateInfo? ReadCachedUpdate()
    {
        if (!Version.TryParse(_settings.AvailableUpdateVersion, out var version) ||
            !Uri.TryCreate(_settings.AvailableUpdateDownloadUrl, UriKind.Absolute, out var downloadUri) ||
            string.IsNullOrWhiteSpace(_settings.AvailableUpdateFileName) ||
            !IsGitHubDownload(downloadUri))
        {
            return null;
        }

        return new UpdateInfo(NormalizeVersion(version), downloadUri, _settings.AvailableUpdateFileName);
    }

    private void SetAvailableUpdate(UpdateInfo update)
    {
        AvailableUpdate = update;
        _settings.AvailableUpdateVersion = update.Version.ToString(3);
        _settings.AvailableUpdateDownloadUrl = update.DownloadUri.AbsoluteUri;
        _settings.AvailableUpdateFileName = update.FileName;
        UpdateAvailabilityChanged?.Invoke(update);
    }

    private void ClearAvailableUpdate()
    {
        var hadUpdate = AvailableUpdate is not null || !string.IsNullOrWhiteSpace(_settings.AvailableUpdateVersion);
        AvailableUpdate = null;
        _settings.AvailableUpdateVersion = null;
        _settings.AvailableUpdateDownloadUrl = null;
        _settings.AvailableUpdateFileName = null;

        if (hadUpdate)
            UpdateAvailabilityChanged?.Invoke(null);
    }

    private static bool IsGitHubDownload(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttps &&
        (uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
         uri.Host.EndsWith(".github.com", StringComparison.OrdinalIgnoreCase));

    private static Version NormalizeVersion(Version version) =>
        new(version.Major, version.Minor, Math.Max(0, version.Build));
}
