namespace Equibles.Web.Models;

/// <summary>
/// Outcome of comparing the running Web portal version against the latest
/// published GitHub Release. Surfaced to every page via <c>ViewData</c>.
/// </summary>
public class VersionCheckResult
{
    public string CurrentVersion { get; set; }
    public string LatestVersion { get; set; }
    public bool UpdateAvailable { get; set; }
    public string ReleaseUrl { get; set; }
}
