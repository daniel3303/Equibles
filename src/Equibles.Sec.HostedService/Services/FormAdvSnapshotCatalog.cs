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
    /// "no data" months) is skipped for the month before it; any other non-roster link, an
    /// ambiguous month or a link outside SEC's file directory refuses.
    /// </summary>
    internal static FormAdvSnapshot Latest(string html, FormAdvRegistration registration)
    {
        foreach (var links in Months(html, registration).OrderByDescending(month => month.Key))
        {
            if (links.Value.Length != 1)
                throw new InvalidDataException(
                    "The newest SEC Form ADV snapshot is ambiguous or is not a roster archive."
                );
            var kind = Classify(Official(links.Value[0]));
            if (kind == LinkKind.NoDataNotice)
                continue;
            if (kind == LinkKind.Other)
                throw new InvalidDataException(
                    "The newest SEC Form ADV snapshot is ambiguous or is not a roster archive."
                );
            return links.Value[0];
        }

        throw new InvalidDataException(
            $"The SEC Form ADV catalogue publishes no {registration} roster archive."
        );
    }

    /// <summary>
    /// Every roster of one kind published on or after <paramref name="since"/>, oldest first. The
    /// "no data" PDF months, ambiguous months and unsupported formats are left out, so a history
    /// walk treats them as months SEC published nothing for; a link outside SEC's file directory
    /// still refuses.
    /// </summary>
    internal static List<FormAdvSnapshot> All(
        string html,
        FormAdvRegistration registration,
        DateOnly since
    ) =>
        Months(html, registration)
            .Where(month => month.Key >= since && month.Value.Length == 1)
            .Select(month => Official(month.Value[0]))
            .Where(snapshot => Classify(snapshot) == LinkKind.Roster)
            .OrderBy(snapshot => snapshot.ReportDate)
            .ToList();

    private enum LinkKind
    {
        Roster,
        NoDataNotice,
        Other,
    }

    // Rosters are ZIP archives (holding a CSV or a workbook) or, from mid-2023 to late 2025, bare
    // .xlsx workbooks.
    private static LinkKind Classify(FormAdvSnapshot snapshot)
    {
        var path = new Uri(snapshot.Url).AbsolutePath;
        if (path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            return LinkKind.NoDataNotice;
        return
            path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)
            ? LinkKind.Roster
            : LinkKind.Other;
    }

    // A link the importer would download must sit in SEC's file directory.
    private static FormAdvSnapshot Official(FormAdvSnapshot snapshot)
    {
        var download = new Uri(snapshot.Url);
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
        return snapshot;
    }

    // Each catalogued month with its distinct links.
    private static Dictionary<DateOnly, FormAdvSnapshot[]> Months(
        string html,
        FormAdvRegistration registration
    )
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

        return snapshots
            .GroupBy(snapshot => snapshot.ReportDate)
            .ToDictionary(month => month.Key, month => month.Distinct().ToArray());
    }
}
