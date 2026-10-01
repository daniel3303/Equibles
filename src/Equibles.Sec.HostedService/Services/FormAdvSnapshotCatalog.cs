using System.Globalization;
using HtmlAgilityPack;

namespace Equibles.Sec.HostedService.Services;

internal static class FormAdvSnapshotCatalog
{
    internal const string PageUrl =
        "https://www.sec.gov/data-research/sec-markets-data/information-about-registered-investment-advisers-exempt-reporting-advisers";
    private const string RegisteredLabel = "Registered Investment Advisers, ";

    internal static FormAdvSnapshot Latest(string html)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);
        var snapshots = new List<FormAdvSnapshot>();
        foreach (
            var link in document.DocumentNode.SelectNodes("//a[@href]")
                ?? new HtmlNodeCollection(null)
        )
        {
            var label = HtmlEntity.DeEntitize(link.InnerText).Trim();
            if (!label.StartsWith(RegisteredLabel, StringComparison.Ordinal))
                continue;
            var month = label[RegisteredLabel.Length..].Split(" - ", 2, StringSplitOptions.None)[0];
            if (
                !DateOnly.TryParseExact(
                    month,
                    "MMMM yyyy",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var date
                )
            )
                throw new InvalidDataException(
                    "The SEC Form ADV catalogue has an unrecognized snapshot month."
                );
            var href = HtmlEntity.DeEntitize(link.GetAttributeValue("href", ""));
            if (!Uri.TryCreate(new Uri(PageUrl), href, out var uri))
                throw new InvalidDataException(
                    "The SEC Form ADV catalogue has an invalid snapshot URL."
                );
            snapshots.Add(new FormAdvSnapshot(date, uri.AbsoluteUri));
        }

        if (snapshots.Count == 0)
            throw new InvalidDataException(
                "The SEC Form ADV catalogue contains no registered-adviser snapshots."
            );
        var newest = snapshots.Max(snapshot => snapshot.ReportDate);
        var latest = snapshots
            .Where(snapshot => snapshot.ReportDate == newest)
            .Distinct()
            .ToArray();
        if (
            latest.Length != 1
            || !new Uri(latest[0].Url).AbsolutePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
        )
            throw new InvalidDataException(
                "The newest SEC Form ADV snapshot is ambiguous or is not a ZIP archive."
            );
        var download = new Uri(latest[0].Url);
        if (
            download.Scheme != "https"
            || download.Host != "www.sec.gov"
            || !download.IsDefaultPort
            || download.UserInfo.Length != 0
            || !download.AbsolutePath.StartsWith("/files/", StringComparison.Ordinal)
        )
            throw new InvalidDataException(
                "The SEC Form ADV catalogue links outside its official file directory."
            );
        return latest[0];
    }
}
