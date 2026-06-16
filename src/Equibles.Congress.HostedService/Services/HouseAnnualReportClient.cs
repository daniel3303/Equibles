using System.IO.Compression;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Equibles.Congress.Data.Models;
using Equibles.Congress.HostedService.Models;
using Equibles.Core.AutoWiring;
using Equibles.Integrations.Common.RateLimiter;
using Equibles.Integrations.Common.Retry;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using static Equibles.Congress.HostedService.Services.DisclosureParsingHelper;

namespace Equibles.Congress.HostedService.Services;

/// <summary>
/// Downloads and parses House Clerk annual financial disclosure reports (Form
/// A originals, index FilingType "O", and amendments, FilingType "A") from the
/// yearly {year}FD.zip index. Only electronically-filed reports carry
/// extractable text — scanned paper filings yield no schedule content and are
/// skipped, so a missing report means "no electronic filing", not zero net
/// worth. Parsing is column-aware: the generated PDFs render Schedule A
/// (assets) and Schedule D (liabilities) as tables whose cells can wrap, and
/// both the asset-value and income columns hold dollar ranges, so cell
/// ownership is decided by word X-positions against the table header — never
/// by "first dollar amount on the line".
/// </summary>
[Service]
public partial class HouseAnnualReportClient
{
    private static readonly IRateLimiter RateLimiter = new RateLimiter(
        maxRequests: 5,
        timeWindow: TimeSpan.FromSeconds(1)
    );
    private const int MaxRetries = 3;

    // Words whose baselines fall within this many PDF points belong to the
    // same visual line when reconstructing the schedule tables from page
    // geometry.
    private const double LineClusterTolerance = 3.0;

    // Column boundaries are the header words' left edges; data words align to
    // the same edges within float noise, so windows start this many points
    // before the header word.
    private const double ColumnTolerance = 2.0;

    private readonly HttpClient _httpClient;
    private readonly ILogger<HouseAnnualReportClient> _logger;

    private const string BaseUrl = "https://disclosures-clerk.house.gov";
    private const string ZipUrlTemplate = BaseUrl + "/public_disc/financial-pdfs/{0}FD.zip";
    private const string AnnualPdfUrlTemplate = BaseUrl + "/public_disc/financial-pdfs/{0}/{1}.pdf";

    // Honorific tokens some House filings inject into the disclosed name, with or
    // without a trailing period ("Mr"/"Mr."/"Dr"). They are not part of the name;
    // left in, they fragment one person into several CongressMember records keyed
    // on the raw name string (e.g. "Mark Dr Green" vs "Mark Green").
    private static readonly HashSet<string> HonorificTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "Mr",
        "Mrs",
        "Ms",
        "Dr",
        "Hon",
    };

    public HouseAnnualReportClient(HttpClient httpClient, ILogger<HouseAnnualReportClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<List<AnnualDisclosureReport>> GetAnnualReports(int year, CancellationToken ct)
    {
        var reports = new List<AnnualDisclosureReport>();

        List<AnnualFiling> filings;
        try
        {
            filings = await DownloadAndParseFilingIndex(year, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to download House FD index for year {Year}", year);
            return reports;
        }

        _logger.LogInformation(
            "Found {Count} House annual report filings for year {Year}",
            filings.Count,
            year
        );

        foreach (var filing in filings)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var report = await DownloadAndParseAnnualPdf(filing, year, ct);
                if (report != null)
                    reports.Add(report);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to parse House annual report PDF for {Member} (DocID {DocId})",
                    filing.MemberName,
                    filing.DocId
                );
            }
        }

        _logger.LogInformation(
            "Parsed {Parsed} electronic House annual reports out of {Total} filings for year {Year}",
            reports.Count,
            filings.Count,
            year
        );
        return reports;
    }

    private async Task<List<AnnualFiling>> DownloadAndParseFilingIndex(
        int year,
        CancellationToken ct
    )
    {
        await RateLimiter.WaitAsync();

        var zipUrl = string.Format(ZipUrlTemplate, year);
        using var response = await _httpClient.GetAsync(zipUrl, ct);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogDebug("House FD ZIP not found for year {Year}", year);
            return [];
        }

        response.EnsureSuccessStatusCode();

        using var zipStream = await response.Content.ReadAsStreamAsync(ct);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);

        var xmlEntry = archive.GetEntry($"{year}FD.xml");
        if (xmlEntry == null)
        {
            _logger.LogWarning("No XML index found in House FD ZIP for year {Year}", year);
            return [];
        }

        await using var xmlStream = xmlEntry.Open();
        var doc = await XDocument.LoadAsync(xmlStream, LoadOptions.None, ct);

        return doc.Descendants("Member")
            .Where(m => m.Element("FilingType")?.Value is "O" or "A")
            .Select(m =>
            {
                string TrimmedField(string elementName) =>
                    m.Element(elementName)?.Value?.Trim() ?? "";

                var filingDateStr = m.Element("FilingDate")?.Value;
                DateOnly.TryParse(
                    filingDateStr,
                    System.Globalization.CultureInfo.GetCultureInfo("en-US"),
                    System.Globalization.DateTimeStyles.None,
                    out var filingDate
                );
                var prefix = TrimmedField("Prefix");
                var first = TrimmedField("First");
                var last = TrimmedField("Last");
                var name = StripHonorificPrefixes($"{prefix} {first} {last}".Trim()).Trim();

                return new AnnualFiling(
                    name,
                    m.Element("DocID")?.Value ?? "",
                    filingDate,
                    m.Element("StateDst")?.Value ?? "",
                    m.Element("FilingType")?.Value == "A"
                );
            })
            .Where(f => !string.IsNullOrEmpty(f.DocId) && !string.IsNullOrEmpty(f.MemberName))
            .ToList();
    }

    private async Task<AnnualDisclosureReport> DownloadAndParseAnnualPdf(
        AnnualFiling filing,
        int year,
        CancellationToken ct
    )
    {
        var pdfUrl = string.Format(AnnualPdfUrlTemplate, year, filing.DocId);
        using var response = await SendWithRetryAsync(pdfUrl, ct);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogDebug("House annual report PDF not found: {Url}", pdfUrl);
            return null;
        }

        response.EnsureSuccessStatusCode();

        var pdfBytes = await response.Content.ReadAsByteArrayAsync(ct);

        HouseAnnualReportContent content;
        try
        {
            content = ParseAnnualReportPdf(pdfBytes);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to read House annual report PDF for {Member} (DocID {DocId})",
                filing.MemberName,
                filing.DocId
            );
            return null;
        }

        if (content == null)
        {
            // No Schedule A header in the extracted text: a scanned paper
            // filing (image-only pages) — the electronic-only policy skips it.
            _logger.LogDebug(
                "House annual report DocID {DocId} for {Member} has no extractable schedules; skipping as non-electronic",
                filing.DocId,
                filing.MemberName
            );
            return null;
        }

        if (!IsMemberFilerStatus(content.FilerStatus))
        {
            // The preamble's own "Status:" field is authoritative: candidate
            // reports ("Congressional Candidate") are not member disclosures
            // and must never enter the member net-worth data.
            _logger.LogDebug(
                "House annual report DocID {DocId} for {Member} has filer status '{Status}'; skipping non-member report",
                filing.DocId,
                filing.MemberName,
                content.FilerStatus
            );
            return null;
        }

        return new AnnualDisclosureReport
        {
            MemberName = filing.MemberName,
            Position = CongressPosition.Representative,
            StateDistrict = filing.StateDistrict,
            Year = year,
            FiledDate = filing.FilingDate,
            ReportId = filing.DocId,
            IsAmendment = filing.IsAmendment,
            Lines = content.Lines,
        };
    }

    // Member and member-elect reports stay; anything else ("Congressional
    // Candidate", "Officer or Employee") is not a member disclosure. A missing
    // status keeps the report — the index only lists annual filing types and
    // dropping data on a preamble-format drift would be worse.
    internal static bool IsMemberFilerStatus(string filerStatus) =>
        filerStatus == null || filerStatus.Contains("Member", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Parses the preamble filer status plus the Schedule A (assets) and
    /// Schedule D (liabilities) rows out of an annual report PDF. Returns null
    /// when the document carries no Schedule A header — the signature of a
    /// scanned (non-electronic) filing.
    /// </summary>
    internal static HouseAnnualReportContent ParseAnnualReportPdf(byte[] pdfBytes)
    {
        using var document = PdfDocument.Open(pdfBytes);
        var lines = new List<List<ScheduleToken>>();
        foreach (var page in document.GetPages())
            lines.AddRange(ExtractTokenLines(page));

        var items = ParseScheduleLines(lines);
        return items == null
            ? null
            : new HouseAnnualReportContent(ExtractFilerStatus(lines), items);
    }

    // The preamble renders "Status: Member" / "Status: Congressional
    // Candidate" before the first schedule header.
    internal static string ExtractFilerStatus(IReadOnlyList<List<ScheduleToken>> lines)
    {
        foreach (var line in lines)
        {
            if (line.Count == 0)
                continue;
            if (MatchScheduleHeader(line) != '\0')
                return null;
            if (line[0].Text == "Status:" && line.Count > 1)
                return string.Join(" ", line.Skip(1).Select(t => t.Text));
        }
        return null;
    }

    // Cluster words by their vertical position into visual lines, then order
    // each line left-to-right, keeping every word's left edge so cells can be
    // assigned to table columns afterwards.
    internal static List<List<ScheduleToken>> ExtractTokenLines(Page page)
    {
        var words = page.GetWords()
            .Where(w => !string.IsNullOrWhiteSpace(w.Text))
            .OrderByDescending(w => w.BoundingBox.Bottom)
            .ToList();

        var lines = new List<List<ScheduleToken>>();
        var current = new List<Word>();
        var currentY = double.NaN;

        foreach (var word in words)
        {
            var y = word.BoundingBox.Bottom;
            if (current.Count == 0 || Math.Abs(y - currentY) <= LineClusterTolerance)
            {
                if (current.Count == 0)
                    currentY = y;
                current.Add(word);
            }
            else
            {
                lines.Add(ToTokenLine(current));
                current = [word];
                currentY = y;
            }
        }

        if (current.Count > 0)
            lines.Add(ToTokenLine(current));

        return lines;
    }

    // Small-caps text (schedule headers, "Location:" / "Description:" labels)
    // extracts its lowercase glyphs as NUL characters, which Trim() does not
    // remove — strip them so "Schedule" reduces to a clean "S" token.
    private static List<ScheduleToken> ToTokenLine(List<Word> words) =>
        words
            .OrderBy(w => w.BoundingBox.Left)
            .Select(w => new ScheduleToken(w.Text.Replace("\0", "").Trim(), w.BoundingBox.Left))
            .ToList();

    /// <summary>
    /// State machine over the reconstructed lines of a whole report. Schedule
    /// headers render in small caps whose lowercase glyphs extract as blanks,
    /// so "Schedule A:" arrives as a bare "S" word followed by "A:"; the
    /// active schedule and its column layout persist across page breaks
    /// (continuation pages repeat the column header for Schedule A but not
    /// necessarily for Schedule D).
    /// </summary>
    internal static List<AnnualDisclosureLineItem> ParseScheduleLines(
        IReadOnlyList<List<ScheduleToken>> lines
    )
    {
        var items = new List<AnnualDisclosureLineItem>();
        var sawAssetSchedule = false;
        var section = '\0';
        AssetColumns assetColumns = null;
        LiabilityColumns liabilityColumns = null;
        PendingLine pending = null;
        // True while inside a "Location:" / "Description:" label paragraph:
        // its wrapped lines carry no label of their own and would otherwise be
        // mistaken for wrapped row-description text.
        var inLabelParagraph = false;

        void Flush()
        {
            var item = pending?.ToLineItem();
            if (item != null)
                items.Add(item);
            pending = null;
        }

        // Decides what a data line contributes: a new row (its range cell
        // holds a complete range, the opening bound of a wrapping range, or a
        // "None" / "Undetermined" sentinel), the continuation of the current
        // row (a bare wrapped upper bound and/or wrapped description text),
        // or page furniture to ignore. Anything in the range window that is
        // not exactly range-shaped is label-paragraph prose spilling across
        // the page (e.g. ".. strike price of $200 and ..") and contributes
        // nothing.
        void ApplyRowText(
            CongressionalDisclosureLineKind kind,
            string rangeText,
            string primaryText,
            string secondaryText
        )
        {
            if (string.IsNullOrEmpty(rangeText))
            {
                if (!inLabelParagraph)
                    pending?.AppendDescription(primaryText, secondaryText);
                return;
            }

            if (rangeText is "None" or "Undetermined")
            {
                // A disclosed row without a dollar range: track it so wrapped
                // description lines attach to it, but never emit a line item.
                inLabelParagraph = false;
                Flush();
                pending = new PendingLine(kind, primaryText, secondaryText, rangeText: null);
                return;
            }

            if (RangeStartRegex().IsMatch(rangeText))
            {
                inLabelParagraph = false;
                Flush();
                pending = new PendingLine(kind, primaryText, secondaryText, rangeText);
                return;
            }

            if (!inLabelParagraph && pending != null && WrappedBoundRegex().IsMatch(rangeText))
            {
                // A lone dollar amount is the wrapped upper bound of the
                // range opened on the previous line.
                pending.AppendRange(rangeText);
                pending.AppendDescription(primaryText, secondaryText);
            }
        }

        foreach (var line in lines)
        {
            if (line.Count == 0)
                continue;

            var scheduleLetter = MatchScheduleHeader(line);
            if (scheduleLetter != '\0')
            {
                Flush();
                section = scheduleLetter;
                if (section == 'A')
                    sawAssetSchedule = true;
                inLabelParagraph = false;
                continue;
            }

            // Field-label lines ("Location:", "Description:", "Digitally
            // Signed:") and footnotes never hold a schedule row — and a label
            // paragraph may wrap onto label-less lines that must not be read
            // as row-description continuations.
            if (line.Any(t => t.Text.Contains(':')) || line[0].Text.StartsWith('*'))
            {
                inLabelParagraph = true;
                continue;
            }

            switch (section)
            {
                case 'A':
                    if (TryReadAssetHeader(line, out var newAssetColumns))
                    {
                        assetColumns = newAssetColumns;
                        continue;
                    }
                    if (assetColumns == null)
                        continue;
                    ApplyRowText(
                        CongressionalDisclosureLineKind.Asset,
                        rangeText: JoinWindow(
                            line,
                            assetColumns.ValueLeft,
                            assetColumns.IncomeLeft
                        ),
                        primaryText: JoinWindow(line, double.MinValue, assetColumns.OwnerLeft),
                        secondaryText: null
                    );
                    break;

                case 'D':
                    if (TryReadLiabilityHeader(line, out var newLiabilityColumns))
                    {
                        liabilityColumns = newLiabilityColumns;
                        continue;
                    }
                    if (liabilityColumns == null)
                        continue;
                    ApplyRowText(
                        CongressionalDisclosureLineKind.Liability,
                        rangeText: JoinWindow(line, liabilityColumns.AmountLeft, double.MaxValue),
                        primaryText: JoinWindow(
                            line,
                            liabilityColumns.TypeLeft,
                            liabilityColumns.AmountLeft
                        ),
                        secondaryText: JoinWindow(
                            line,
                            liabilityColumns.CreditorLeft,
                            liabilityColumns.DateLeft
                        )
                    );
                    break;
            }
        }

        Flush();
        return sawAssetSchedule ? items : null;
    }

    // The schedule headers' lowercase small-cap glyphs extract as whitespace,
    // leaving a bare "S" word followed by the section letter and colon
    // ("S A:" … "S I:"). Section addenda ("Schedule A and B Investment
    // Vehicle Details") reduce to colon-less letters and must not match.
    private static char MatchScheduleHeader(List<ScheduleToken> line)
    {
        if (line.Count < 2 || line[0].Text != "S")
            return '\0';
        var second = line[1].Text;
        return second.Length == 2 && second[1] == ':' && second[0] is >= 'A' and <= 'I'
            ? second[0]
            : '\0';
    }

    // Schedule A column header: "Asset | Owner | Value of Asset | Income
    // Type(s) | Income | Tx. >". Both the value and the income columns carry
    // dollar ranges, so the asset-value window must end where the income-type
    // column starts.
    private static bool TryReadAssetHeader(List<ScheduleToken> line, out AssetColumns columns)
    {
        columns = null;
        if (line[0].Text != "Asset")
            return false;

        var owner = line.FirstOrDefault(t => t.Text == "Owner");
        var value = line.FirstOrDefault(t => t.Text == "Value");
        if (owner == null || value == null)
            return false;

        var income = line.FirstOrDefault(t => t.Text == "Income" && t.Left > value.Left);
        if (income == null)
            return false;

        columns = new AssetColumns(owner.Left, value.Left, income.Left);
        return true;
    }

    // Schedule D column header: "Owner | Creditor | Date Incurred | Type |
    // Amount of Liability".
    private static bool TryReadLiabilityHeader(
        List<ScheduleToken> line,
        out LiabilityColumns columns
    )
    {
        columns = null;
        if (line[0].Text != "Owner")
            return false;

        var creditor = line.FirstOrDefault(t => t.Text == "Creditor");
        var date = line.FirstOrDefault(t => t.Text == "Date");
        var type = line.FirstOrDefault(t => t.Text == "Type");
        var amount = line.FirstOrDefault(t => t.Text == "Amount");
        if (creditor == null || date == null || type == null || amount == null)
            return false;

        columns = new LiabilityColumns(creditor.Left, date.Left, type.Left, amount.Left);
        return true;
    }

    private static string JoinWindow(List<ScheduleToken> line, double fromLeft, double toLeft)
    {
        var lower = fromLeft == double.MinValue ? double.MinValue : fromLeft - ColumnTolerance;
        var upper = toLeft == double.MaxValue ? double.MaxValue : toLeft - ColumnTolerance;
        return string.Join(
            " ",
            line.Where(t => t.Left >= lower && t.Left < upper).Select(t => t.Text)
        );
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(string url, CancellationToken ct)
    {
        for (var attempt = 0; attempt <= MaxRetries; attempt++)
        {
            await RateLimiter.WaitAsync();
            var response = await _httpClient.GetAsync(url, ct);

            if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt < MaxRetries)
            {
                var delay = RetryBackoff.Exponential(attempt);
                _logger.LogWarning(
                    "House disclosure rate limited (429), retrying in {Delay}s",
                    delay.TotalSeconds
                );
                RateLimiter.PauseFor(delay);
                response.Dispose();
                await Task.Delay(delay, ct);
                continue;
            }

            if ((int)response.StatusCode >= 500 && attempt < MaxRetries)
            {
                var delay = RetryBackoff.Exponential(attempt);
                _logger.LogWarning(
                    "House disclosure server error ({StatusCode}), retrying in {Delay}s",
                    (int)response.StatusCode,
                    delay.TotalSeconds
                );
                response.Dispose();
                await Task.Delay(delay, ct);
                continue;
            }

            return response;
        }

        throw new HttpRequestException(
            $"Max retries ({MaxRetries}) exceeded for House disclosure request: {url}"
        );
    }

    // Normalises a House-disclosed member name so cosmetic variants resolve to one
    // identity: drops honorific tokens in any position (period-agnostic) and
    // collapses an immediately repeated token (the source occasionally doubles the
    // first name, e.g. "Scott Scott Franklin"). Whole-token matching keeps real
    // names such as "Mraz" intact.
    private static string StripHonorificPrefixes(string name)
    {
        var tokens = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var result = new List<string>(tokens.Length);
        foreach (var token in tokens)
        {
            if (HonorificTokens.Contains(token.TrimEnd('.')))
                continue;
            if (
                result.Count > 0
                && string.Equals(result[^1], token, StringComparison.OrdinalIgnoreCase)
            )
                continue;
            result.Add(token);
        }

        return string.Join(' ', result);
    }

    // Bracketed asset-type code such as [ST], [OP], [BA] — a code, not part of
    // the asset's name.
    [GeneratedRegex(@"\s*\[[A-Za-z]{1,3}\]\s*")]
    private static partial Regex AssetTypeCodeRegex();

    // The exact shapes a range cell opens a row with: "$X -" (upper bound
    // wraps), "$X - $Y", the open-top brackets "Over $X" / "$X +".
    [GeneratedRegex(@"^(?:\$[\d,]+ -(?: \$[\d,]+)?|Over \$[\d,]+|\$[\d,]+ \+)$")]
    private static partial Regex RangeStartRegex();

    // A wrapped range upper bound: the bare dollar amount and nothing else.
    [GeneratedRegex(@"^\$[\d,]+$")]
    private static partial Regex WrappedBoundRegex();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex RepeatedWhitespaceRegex();

    internal sealed record ScheduleToken(string Text, double Left);

    /// <summary>
    /// An e-filed annual report's extracted content: the preamble's filer
    /// status ("Member", "Congressional Candidate", …) and the asset/liability
    /// line items.
    /// </summary>
    internal sealed record HouseAnnualReportContent(
        string FilerStatus,
        List<AnnualDisclosureLineItem> Lines
    );

    private sealed record AnnualFiling(
        string MemberName,
        string DocId,
        DateOnly FilingDate,
        string StateDistrict,
        bool IsAmendment
    );

    private sealed record AssetColumns(double OwnerLeft, double ValueLeft, double IncomeLeft);

    private sealed record LiabilityColumns(
        double CreditorLeft,
        double DateLeft,
        double TypeLeft,
        double AmountLeft
    );

    // Accumulates one schedule row while its wrapped cells arrive: the range
    // cell can wrap its upper bound onto the next visual line, and the
    // description cells (asset name; liability type and creditor) wrap freely.
    private sealed class PendingLine
    {
        private readonly CongressionalDisclosureLineKind _kind;
        private string _primary;
        private string _secondary;
        private string _rangeText;

        public PendingLine(
            CongressionalDisclosureLineKind kind,
            string primaryText,
            string secondaryText,
            string rangeText
        )
        {
            _kind = kind;
            _primary = primaryText ?? "";
            _secondary = secondaryText ?? "";
            _rangeText = rangeText;
        }

        public void AppendRange(string text)
        {
            if (_rangeText != null)
                _rangeText += " " + text;
        }

        public void AppendDescription(string primaryText, string secondaryText)
        {
            if (!string.IsNullOrEmpty(primaryText))
                _primary += " " + primaryText;
            if (!string.IsNullOrEmpty(secondaryText))
                _secondary += " " + secondaryText;
        }

        public AnnualDisclosureLineItem ToLineItem()
        {
            if (_rangeText == null)
                return null;

            var (minimum, maximum) = ParseAmountRange(_rangeText);
            if (minimum == 0 && maximum == 0)
                return null;

            var description = BuildDescription();
            if (string.IsNullOrEmpty(description))
                return null;

            return new AnnualDisclosureLineItem
            {
                Kind = _kind,
                Description = Truncate(description, 512),
                RangeMinimum = minimum,
                RangeMaximum = maximum,
            };
        }

        private string BuildDescription()
        {
            var primary = Clean(_primary);
            var secondary = Clean(_secondary);
            if (string.IsNullOrEmpty(primary))
                return secondary;
            return string.IsNullOrEmpty(secondary) ? primary : $"{primary} ({secondary})";
        }

        private static string Clean(string text) =>
            RepeatedWhitespaceRegex().Replace(AssetTypeCodeRegex().Replace(text, " "), " ").Trim();
    }
}
