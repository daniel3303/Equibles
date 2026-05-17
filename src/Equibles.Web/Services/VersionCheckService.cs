using System.Reflection;
using Equibles.Core.AutoWiring;
using Equibles.Web.Models;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;

namespace Equibles.Web.Services;

/// <summary>
/// Checks the running Web portal version against the latest published GitHub
/// Release. The result is cached in memory and refreshed off the request path
/// (a request is never blocked on the network) and the check fails silently —
/// no telemetry is sent and errors are never surfaced to the operator.
/// </summary>
[Service(ServiceLifetime.Singleton)]
public class VersionCheckService
{
    private const string ReleaseApiUrl =
        "https://api.github.com/repos/daniel3303/Equibles/releases/latest";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(6);

    // After a failed check, retry sooner than the full TTL so a transient
    // GitHub outage doesn't blind the banner for hours.
    private static readonly TimeSpan FailureBackoff = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(3);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<VersionCheckService> _logger;

    private volatile VersionCheckResult _cached;
    private long _cachedAtTicks;
    private int _refreshing;

    public VersionCheckService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<VersionCheckService> logger
    )
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Returns the last known check result immediately. When the cache is empty
    /// or stale a background refresh is kicked off and a "no update" result is
    /// returned meanwhile, so callers never wait on the network.
    /// </summary>
    public VersionCheckResult Get()
    {
        var current = GetCurrentVersion();

        if (!_configuration.GetValue("CheckForUpdates", true))
        {
            return new VersionCheckResult { CurrentVersion = current, UpdateAvailable = false };
        }

        // Read the timestamp first, then the payload. Writers publish in the
        // opposite order (payload, then timestamp via Interlocked), so a fresh
        // timestamp guarantees an at-least-as-fresh payload.
        var cachedAtTicks = Interlocked.Read(ref _cachedAtTicks);
        var cached = _cached;
        if (cached != null && DateTime.UtcNow.Ticks - cachedAtTicks < CacheTtl.Ticks)
        {
            return cached;
        }

        TriggerRefresh();
        return cached
            ?? new VersionCheckResult { CurrentVersion = current, UpdateAvailable = false };
    }

    // Ensures only one outbound GitHub request runs at a time.
    private void TriggerRefresh()
    {
        if (Interlocked.CompareExchange(ref _refreshing, 1, 0) != 0)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Refresh();
            }
            finally
            {
                Interlocked.Exchange(ref _refreshing, 0);
            }
        });
    }

    private async Task Refresh()
    {
        var current = GetCurrentVersion();
        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = HttpTimeout;

            using var request = new HttpRequestMessage(HttpMethod.Get, ReleaseApiUrl);
            request.Headers.UserAgent.ParseAdd("Equibles");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");

            var token = _configuration["GitHubToken"];
            if (!string.IsNullOrEmpty(token))
            {
                request.Headers.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            }

            using var response = await client.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var release = JsonConvert.DeserializeObject<GitHubRelease>(json);
            var latest = NormalizeVersion(release?.TagName);

            StoreResult(
                new VersionCheckResult
                {
                    CurrentVersion = current,
                    LatestVersion = latest,
                    UpdateAvailable = IsNewer(current, latest),
                    ReleaseUrl = release?.HtmlUrl,
                },
                CacheTtl
            );
        }
        catch (Exception ex)
        {
            // Fail silent: never surface update-check failures to the operator.
            // Keep the last good result if we have one (a transient outage must
            // not hide a real update) and retry sooner than the full TTL.
            _logger.LogDebug(ex, "Update check against GitHub Releases failed");
            var fallback =
                _cached
                ?? new VersionCheckResult { CurrentVersion = current, UpdateAvailable = false };
            StoreResult(fallback, FailureBackoff);
        }
    }

    // Publishes the payload first, then the timestamp via Interlocked, so a
    // reader that observes a fresh timestamp also sees the matching payload.
    // <paramref name="freshFor"/> controls how long the entry counts as fresh.
    private void StoreResult(VersionCheckResult result, TimeSpan freshFor)
    {
        _cached = result;
        var effectiveTicks = DateTime.UtcNow.Ticks - (CacheTtl - freshFor).Ticks;
        Interlocked.Exchange(ref _cachedAtTicks, effectiveTicks);
    }

    // Compares Major.Minor.Build only so tag formatting variance
    // (e.g. "v1.0.0" vs an assembly "1.0.0.0") can't trigger a spurious banner.
    private static bool IsNewer(string current, string latest)
    {
        if (
            !Version.TryParse(NormalizeVersion(current), out var currentVersion)
            || !Version.TryParse(latest, out var latestVersion)
        )
        {
            return false;
        }

        static Version ToCore(Version v) => new(v.Major, v.Minor, v.Build < 0 ? 0 : v.Build);

        return ToCore(latestVersion) > ToCore(currentVersion);
    }

    private static string GetCurrentVersion()
    {
        var assembly = typeof(VersionCheckService).Assembly;
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        if (!string.IsNullOrEmpty(informational))
        {
            return NormalizeVersion(informational);
        }

        return assembly.GetName().Version?.ToString();
    }

    // Strips a leading "v" and any pre-release/build metadata so the value
    // parses with System.Version (tags look like "v1.0.0").
    private static string NormalizeVersion(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        var normalized = value.Trim();
        if (normalized.StartsWith('v') || normalized.StartsWith('V'))
        {
            normalized = normalized[1..];
        }

        var cut = normalized.IndexOfAny(['-', '+']);
        if (cut >= 0)
        {
            normalized = normalized[..cut];
        }

        return normalized;
    }
}
