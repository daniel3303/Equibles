namespace Equibles.Core.Extensions;

public static class RegistrableDomain
{
    private static readonly HashSet<string> CountryCodeSecondLevels =
    [
        "co",
        "com",
        "org",
        "net",
        "ac",
        "gov",
        "edu",
    ];

    /// <summary>
    /// The registrable domain a lower-case host belongs to, so ir.acme.com and www.acme.com are one
    /// site: its last two labels, or three when a two-letter country code follows a common
    /// second-level label (amada.co.jp, never "co.jp", which would merge every company under it).
    /// </summary>
    public static string Of(string host)
    {
        var labels = host.Split('.');
        var take =
            labels.Length >= 3
            && labels[^1].Length == 2
            && CountryCodeSecondLevels.Contains(labels[^2])
                ? 3
                : 2;
        return labels.Length <= take ? host : string.Join('.', labels[^take..]);
    }
}
