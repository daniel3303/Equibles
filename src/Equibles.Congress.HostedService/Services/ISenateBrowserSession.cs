namespace Equibles.Congress.HostedService.Services;

/// <summary>
/// Browser-mediated transport for Senate eFD. Reusing a real browser's TLS
/// fingerprint and session cookies is what bypasses Akamai bot detection, so
/// this concern is isolated here; <see cref="SenateDisclosureClient"/> owns the
/// retry, pagination, and parsing logic on top of it.
/// </summary>
public interface ISenateBrowserSession : IAsyncDisposable
{
    /// <summary>
    /// Launches the browser (once), navigates to Senate eFD, and accepts the
    /// prohibition-agreement disclaimer. Idempotent.
    /// </summary>
    Task EnsureAuthenticated(CancellationToken ct);

    /// <summary>
    /// Issues a request through the authenticated browser context. Pass
    /// <paramref name="formFields"/> for a POST, or null for a GET. Throws
    /// <see cref="SenateBrowserException"/> on a transport failure.
    /// </summary>
    Task<SenateFetchResult> Fetch(
        string url,
        Dictionary<string, string> formFields,
        CancellationToken ct
    );
}
