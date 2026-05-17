using Newtonsoft.Json;

namespace Equibles.Web.Models;

/// <summary>
/// Minimal projection of the GitHub "latest release" API response
/// (<c>/repos/{owner}/{repo}/releases/latest</c>).
/// </summary>
public class GitHubRelease
{
    [JsonProperty("tag_name")]
    public string TagName { get; set; }

    [JsonProperty("html_url")]
    public string HtmlUrl { get; set; }
}
