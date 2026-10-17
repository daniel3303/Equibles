using System.Globalization;
using System.Text.RegularExpressions;
using Equibles.Sec.HostedService.Models;
using HtmlAgilityPack;

namespace Equibles.Sec.HostedService.Services;

/// <summary>
/// Reads SEC's Form N-CEN data set catalogue. Each quarter is one archive named
/// "YYYYqN_ncen.zip"; a re-posted quarter carries a suffix such as "_0", so the file name, not the
/// quarter, identifies the content.
/// </summary>
internal static partial class NCenDataSetCatalog
{
    internal const string PageUrl =
        "https://www.sec.gov/data-research/sec-markets-data/form-n-cen-data-sets";

    [GeneratedRegex(
        @"^/files/dera/data/form-n-cen-data-sets/(?<year>\d{4})q(?<quarter>[1-4])_ncen(?:_\d+)?\.zip$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    )]
    private static partial Regex ArchivePath();

    /// <summary>Every listed quarter, oldest first.</summary>
    internal static List<NCenDataSetLink> Parse(string html)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);
        var byQuarter = new Dictionary<(int Year, int Quarter), NCenDataSetLink>();
        foreach (
            var link in document.DocumentNode.SelectNodes("//a[@href]")
                ?? new HtmlNodeCollection(null)
        )
        {
            var href = HtmlEntity.DeEntitize(link.GetAttributeValue("href", ""));
            if (!Uri.TryCreate(new Uri(PageUrl), href, out var uri))
                continue;
            var match = ArchivePath().Match(uri.AbsolutePath);
            if (!match.Success)
                continue;
            if (
                uri.Scheme != "https"
                || uri.Host != "www.sec.gov"
                || !uri.IsDefaultPort
                || uri.UserInfo.Length != 0
            )
                throw new InvalidDataException(
                    "The SEC N-CEN catalogue links outside its official file directory."
                );

            var key = (
                int.Parse(match.Groups["year"].Value, CultureInfo.InvariantCulture),
                int.Parse(match.Groups["quarter"].Value, CultureInfo.InvariantCulture)
            );
            var dataSet = new NCenDataSetLink(
                key.Item1,
                key.Item2,
                uri.Segments[^1],
                uri.GetLeftPart(UriPartial.Path)
            );
            if (byQuarter.TryGetValue(key, out var listed) && listed != dataSet)
                throw new InvalidDataException(
                    $"The SEC N-CEN catalogue lists two archives for {key.Item1} Q{key.Item2}."
                );
            byQuarter[key] = dataSet;
        }

        if (byQuarter.Count == 0)
            throw new InvalidDataException("The SEC N-CEN catalogue lists no data sets.");
        return byQuarter
            .Values.OrderBy(dataSet => dataSet.Year)
            .ThenBy(dataSet => dataSet.Quarter)
            .ToList();
    }
}
