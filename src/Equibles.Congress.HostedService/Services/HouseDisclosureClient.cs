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

[Service]
public partial class HouseDisclosureClient
{
    private static readonly IRateLimiter RateLimiter = new RateLimiter(
        maxRequests: 5,
        timeWindow: TimeSpan.FromSeconds(1)
    );
    private const int MaxRetries = 3;

    // Words whose baselines fall within this many PDF points belong to the
    // same visual line when reconstructing the PTR table from page geometry.
    private const double LineClusterTolerance = 3.0;

    private readonly HttpClient _httpClient;
    private readonly ILogger<HouseDisclosureClient> _logger;

    private const string BaseUrl = "https://disclosures-clerk.house.gov";
    private const string ZipUrlTemplate = BaseUrl + "/public_disc/financial-pdfs/{0}FD.zip";
    private const string PtrPdfUrlTemplate = BaseUrl + "/public_disc/ptr-pdfs/{0}/{1}.pdf";

    private static readonly string[] HonorificPrefixes = ["Hon. ", "Mr. ", "Mrs. ", "Ms. "];

    public HouseDisclosureClient(HttpClient httpClient, ILogger<HouseDisclosureClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<List<DisclosureTransaction>> GetRecentTransactions(
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken ct
    )
    {
        var transactions = new List<DisclosureTransaction>();
        var years = Enumerable.Range(fromDate.Year, toDate.Year - fromDate.Year + 1);

        foreach (var year in years)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var filings = await DownloadAndParseFilingIndex(year, fromDate, toDate, ct);
                _logger.LogInformation(
                    "Found {Count} House PTR filings for year {Year}",
                    filings.Count,
                    year
                );

                foreach (var filing in filings)
                {
                    try
                    {
                        ct.ThrowIfCancellationRequested();
                        var txns = await DownloadAndParsePtrPdf(filing, year, ct);
                        transactions.AddRange(txns);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(
                            ex,
                            "Failed to parse House PTR PDF for {Member} (DocID {DocId})",
                            filing.MemberName,
                            filing.DocId
                        );
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to download House filing index for year {Year}",
                    year
                );
            }
        }

        _logger.LogInformation(
            "Parsed {Count} transactions from House PTR filings",
            transactions.Count
        );
        return transactions;
    }

    private async Task<List<HouseFiling>> DownloadAndParseFilingIndex(
        int year,
        DateOnly from,
        DateOnly to,
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
            .Where(m => m.Element("FilingType")?.Value == "P")
            .Select(m =>
            {
                string TrimmedField(string elementName) =>
                    m.Element(elementName)?.Value?.Trim() ?? "";

                var filingDateStr = m.Element("FilingDate")?.Value;
                DateOnly.TryParse(filingDateStr, out var filingDate);
                var prefix = TrimmedField("Prefix");
                var first = TrimmedField("First");
                var last = TrimmedField("Last");
                var name = StripHonorificPrefixes($"{prefix} {first} {last}".Trim()).Trim();

                return new HouseFiling(
                    name,
                    m.Element("DocID")?.Value ?? "",
                    filingDate,
                    m.Element("StateDst")?.Value ?? ""
                );
            })
            .Where(f =>
                !string.IsNullOrEmpty(f.DocId)
                && !string.IsNullOrEmpty(f.MemberName)
                && f.FilingDate >= from
                && f.FilingDate <= to
            )
            .ToList();
    }

    private async Task<List<DisclosureTransaction>> DownloadAndParsePtrPdf(
        HouseFiling filing,
        int year,
        CancellationToken ct
    )
    {
        await RateLimiter.WaitAsync();

        var pdfUrl = string.Format(PtrPdfUrlTemplate, year, filing.DocId);
        using var response = await SendWithRetryAsync(pdfUrl, ct);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogDebug("House PTR PDF not found: {Url}", pdfUrl);
            return [];
        }

        response.EnsureSuccessStatusCode();

        var pdfBytes = await response.Content.ReadAsByteArrayAsync(ct);
        return ParsePtrPdf(pdfBytes, filing);
    }

    private List<DisclosureTransaction> ParsePtrPdf(byte[] pdfBytes, HouseFiling filing)
    {
        try
        {
            return ParsePtrPdf(pdfBytes, filing.MemberName, filing.FilingDate);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to read House PTR PDF for {Member} (DocID {DocId})",
                filing.MemberName,
                filing.DocId
            );
            return [];
        }
    }

    // PdfPig's page.Text concatenates glyphs with no line breaks, so the PTR
    // transaction table collapses into one string that a split-on-'\n' parser
    // can never segment. Rebuild the visual lines from word positions first,
    // then parse those.
    internal static List<DisclosureTransaction> ParsePtrPdf(
        byte[] pdfBytes,
        string memberName,
        DateOnly filingDate
    )
    {
        using var document = PdfDocument.Open(pdfBytes);
        var lines = new List<string>();
        foreach (var page in document.GetPages())
            lines.AddRange(ExtractLines(page));
        return ParseTransactionLines(lines, memberName, filingDate);
    }

    // Cluster words by their vertical position into visual lines, then order
    // each line left-to-right. PdfPig separates table columns with real word
    // gaps, so this recovers the "Owner Asset … Type Date NotificationDate
    // Amount" row layout that page.Text destroys.
    internal static List<string> ExtractLines(Page page)
    {
        var words = page.GetWords()
            .Where(w => !string.IsNullOrWhiteSpace(w.Text))
            .OrderByDescending(w => w.BoundingBox.Bottom)
            .ToList();

        var lines = new List<string>();
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
                lines.Add(JoinWords(current));
                current = [word];
                currentY = y;
            }
        }

        if (current.Count > 0)
            lines.Add(JoinWords(current));

        return lines;
    }

    private static string JoinWords(List<Word> words) =>
        string.Join(" ", words.OrderBy(w => w.BoundingBox.Left).Select(w => w.Text.Trim()));

    // A transaction row carries its transaction-type marker and dates on the
    // line that starts it; the asset name (and sometimes the upper amount
    // bound) wrap onto following lines. The owner code, when present, is the
    // first token of the asset text — member-owned holdings leave it blank,
    // which the original owner-code-anchored parser silently dropped.
    internal static List<DisclosureTransaction> ParseTransactionLines(
        IReadOnlyList<string> lines,
        string memberName,
        DateOnly filingDate
    )
    {
        var transactions = new List<DisclosureTransaction>();

        for (var i = 0; i < lines.Count; i++)
        {
            // Field-label lines ("Filing Status:", "Description:") and the
            // table header carry a colon; real transaction rows never do.
            if (lines[i].Contains(':'))
                continue;

            var anchor = TransactionAnchorRegex().Match(lines[i]);
            if (!anchor.Success)
                continue;

            var assetText = lines[i][..anchor.Index];
            var amountText = lines[i][anchor.Index..];

            for (var j = i + 1; j < lines.Count && IsContinuationLine(lines[j]); j++)
            {
                var (assetPart, amountPart) = SplitContinuation(lines[j]);
                if (!string.IsNullOrEmpty(assetPart))
                    assetText += " " + assetPart;
                if (!string.IsNullOrEmpty(amountPart))
                    amountText += " " + amountPart;
            }

            var transaction = BuildTransaction(
                assetText,
                amountText,
                anchor,
                memberName,
                filingDate
            );
            if (transaction != null)
                transactions.Add(transaction);
        }

        return transactions;
    }

    // Lines that continue the current row's asset name or amount: not the next
    // transaction, not a field-label line, not the footer note, not a reprinted header.
    private static bool IsContinuationLine(string line)
    {
        if (
            string.IsNullOrWhiteSpace(line)
            || line.Contains(':')
            || line.StartsWith('*')
            || IsTableHeaderFragment(line)
        )
            return false;
        return !TransactionAnchorRegex().IsMatch(line);
    }

    // Distinctive PTR column labels. A real asset/amount continuation line never carries
    // several of them together, so their co-occurrence flags a reprinted header.
    private static readonly string[] PtrHeaderTokens =
    [
        "Owner",
        "Asset",
        "Transaction",
        "Notification",
        "Amount",
        "Gains",
    ];

    // At a page break the House PTR reprints its column-header block ("ID Owner Asset
    // Transaction Date Notification Amount Cap. Gains > $200? ..."). PdfPig's geometry turns
    // that into a line with no colon, asterisk or transaction anchor, so it would otherwise be
    // mistaken for a continuation and spliced into the preceding trade — appending the header
    // words to the asset name and pulling the "$200" cap-gains threshold into the amount,
    // yielding inverted ranges like "$50,001-$200" (#3378). Skip any line carrying several of
    // the column labels.
    private static bool IsTableHeaderFragment(string line) =>
        PtrHeaderTokens.Count(token => line.Contains(token, StringComparison.OrdinalIgnoreCase))
        >= 3;

    // A continuation line holds asset text, a wrapped amount ("$5,000,000"), or
    // both; the amount is always the trailing "$…" run.
    private static (string asset, string amount) SplitContinuation(string line)
    {
        var dollar = line.IndexOf('$');
        return dollar < 0 ? (line.Trim(), null) : (line[..dollar].Trim(), line[dollar..].Trim());
    }

    private static DisclosureTransaction BuildTransaction(
        string assetText,
        string amountText,
        Match anchor,
        string memberName,
        DateOnly filingDate
    )
    {
        var txType = ParseTransactionType(anchor.Groups[1].Value);
        if (txType == null)
            return null;

        var txDate = ParseDate(anchor.Groups[2].Value);
        if (txDate == null)
            return null;

        assetText = assetText.Trim();
        string owner = null;
        var ownerMatch = OwnerCodeRegex().Match(assetText);
        if (ownerMatch.Success)
        {
            owner = ownerMatch.Groups[1].Value.ToUpperInvariant();
            assetText = assetText[ownerMatch.Length..].Trim();
        }

        // Drop the bracketed asset-type code ("[ST]", "[OP]", …) so it can't be
        // mistaken for a ticker when the asset has no parenthesised symbol.
        assetText = AssetTypeCodeRegex().Replace(assetText, " ").Trim();

        var ticker = ExtractTickerFromAssetName(assetText);
        if (string.IsNullOrEmpty(assetText) && string.IsNullOrEmpty(ticker))
            return null;

        var (amountFrom, amountTo) = ParseAmountRange(amountText);

        return new DisclosureTransaction
        {
            MemberName = memberName,
            Position = CongressPosition.Representative,
            Ticker = ticker,
            AssetName = Truncate(assetText, 256),
            TransactionDate = txDate.Value,
            FilingDate = filingDate,
            TransactionType = txType.Value,
            OwnerType = owner,
            AmountFrom = amountFrom,
            AmountTo = amountTo,
        };
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(string url, CancellationToken ct)
    {
        for (var attempt = 0; attempt <= MaxRetries; attempt++)
        {
            await RateLimiter.WaitAsync();
            var response = await _httpClient.GetAsync(url, ct);

            if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt < MaxRetries)
            {
                var delay = ExponentialBackoff(attempt);
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
                var delay = ExponentialBackoff(attempt);
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

    // Thin forwarder so existing reflection-based backoff tests still find the method.
    private static TimeSpan ExponentialBackoff(int attempt) => RetryBackoff.Exponential(attempt);

    // Owner codes that prefix the asset text: SP (Spouse), JT (Joint), DC
    // (Dependent Child). Member-owned holdings leave the column blank.
    [GeneratedRegex(@"^(SP|JT|DC)\b", RegexOptions.IgnoreCase)]
    private static partial Regex OwnerCodeRegex();

    // The transaction-type marker (P, S, S (partial), S (full)) immediately
    // followed by its transaction date — the anchor identifying the line that
    // starts a transaction row. Group 1 = type, group 2 = MM/DD/YYYY date.
    [GeneratedRegex(@"(?<=\s|^)(S \(partial\)|S \(full\)|S|P)\s+(\d{2}/\d{2}/\d{4})")]
    private static partial Regex TransactionAnchorRegex();

    // Bracketed asset-type code such as [ST], [OP], [OI].
    [GeneratedRegex(@"\s*\[[A-Za-z]{1,3}\]\s*")]
    private static partial Regex AssetTypeCodeRegex();

    private record HouseFiling(
        string MemberName,
        string DocId,
        DateOnly FilingDate,
        string StateDst
    );

    private static string StripHonorificPrefixes(string name)
    {
        foreach (var prefix in HonorificPrefixes)
            name = name.Replace(prefix, "");
        return name;
    }
}
