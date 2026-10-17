using System.Globalization;
using Equibles.Sec.Data.Models;
using HtmlAgilityPack;

namespace Equibles.Sec.HostedService.Services;

internal static class FormAdvSnapshotCatalog
{
    internal const string PageUrl =
        "https://www.sec.gov/data-research/sec-markets-data/information-about-registered-investment-advisers-exempt-reporting-advisers";
    private const string RegisteredLabel = "Registered Investment Advisers, ";
    private const string ExemptLabel = "Exempt Investment Advisers, ";

    /// <summary>
    /// The newest published roster of one kind. A month SEC published as a PDF notice (the
    /// "no data" months) is skipped for the month before it; any other non-ZIP link, an
    /// ambiguous month or a link outside SEC's file directory refuses.
    /// </summary>
    internal static FormAdvSnapshot Latest(string html, FormAdvRegistration registration)
    {
        var label = registration == FormAdvRegistration.Registered ? RegisteredLabel : ExemptLabel;
        var document = new HtmlDocument();
        document.LoadHtml(html);
        var snapshots = new List<FormAdvSnapshot>();
        foreach (
            var link in document.DocumentNode.SelectNodes("//a[@href]")
                ?? new HtmlNodeCollection(null)
        )
        {
            var text = HtmlEntity.DeEntitize(link.InnerText).Trim();
            if (!text.StartsWith(label, StringComparison.Ordinal))
                continue;
            var month = text[label.Length..].Split(" - ", 2, StringSplitOptions.None)[0];
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
                $"The SEC Form ADV catalogue contains no {registration} snapshots."
            );

        foreach (
            var links in snapshots
                .GroupBy(snapshot => snapshot.ReportDate)
                .OrderByDescending(group => group.Key)
                .Select(month => month.Distinct().ToArray())
        )
        {
            if (links.Length != 1)
                throw new InvalidDataException(
                    "The newest SEC Form ADV snapshot is ambiguous or is not a ZIP archive."
                );
            var download = new Uri(links[0].Url);
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
            if (download.AbsolutePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!download.AbsolutePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    "The newest SEC Form ADV snapshot is ambiguous or is not a ZIP archive."
                );
            return links[0];
        }

        throw new InvalidDataException(
            $"The SEC Form ADV catalogue publishes no {registration} roster archive."
        );
    }
}
