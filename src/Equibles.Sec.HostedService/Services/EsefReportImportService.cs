using Equibles.CommonStocks.BusinessLogic;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.AutoWiring;
using Equibles.Integrations.XbrlFilings;
using Equibles.Integrations.XbrlFilings.Models;
using Equibles.Sec.BusinessLogic;
using Equibles.Sec.Data.Models;
using Equibles.Sec.HostedService.Configuration;
using Equibles.Sec.HostedService.Contracts;
using Equibles.Sec.HostedService.Models;
using Equibles.Sec.Repositories;
using Equibles.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Equibles.Sec.HostedService.Services;

/// <summary>
/// Stores the latest European annual report of every verified issuer we hold, as a document carrying the
/// report's own XBRL envelope. Nothing here extracts a fact: the extraction sweep selects any captured
/// envelope, so storing the document is the whole of this lane's work.
/// </summary>
[Service]
public class EsefReportImportService(
    XbrlFilingsClient client,
    EquityIssuerRepository issuerRepository,
    DocumentRepository documentRepository,
    IDocumentPersistenceService documentPersistence,
    EquityIdentityManager identityManager,
    ISecDocumentHtmlNormalizer normalizer,
    ISecDocumentHtmlToMarkdownConverter converter,
    IOptions<EsefReportScraperOptions> options,
    ILogger<EsefReportImportService> logger
) : IImporter
{
    // The index serves a hundred rows a page, which is what its own examples use.
    internal const int IndexPageSize = 100;

    // A guard on the enumeration rather than a budget: the whole corpus was 25,912 filings when this was
    // written, so a pass that keeps asking for pages past this is reading something other than that index.
    internal const int MaxIndexPages = 2_000;

    /// <summary>
    /// The largest report this lane stores, equal to the extraction sweep's own parse ceiling. A report
    /// past it yields no fact and the reader refuses it too, so storing it would only spend the issuer's
    /// one accession on bytes nothing reads. It is skipped and counted instead, which leaves the issuer
    /// eligible again if that ceiling ever rises. Pinned equal to the extractor's by test.
    /// </summary>
    internal const long MaxReportBytes = 50L * 1024 * 1024;

    // Document.SourceUrl is 500 characters wide. A longer address would throw inside the save, be caught
    // as a per-issuer failure and be retried every cycle for ever, so it is refused before the fetch.
    internal const int MaxSourceUrlLength = 500;

    public async Task Import(CancellationToken cancellationToken)
    {
        var issuers = await LoadCandidateIssuers(cancellationToken);
        if (issuers.Count == 0)
        {
            logger.LogInformation(
                "No verified issuer carries a legal entity identifier without a CIK; nothing to capture."
            );
            return;
        }

        var filings = await ReadCorpus(issuers.Keys, cancellationToken);
        var stored = await LoadStoredReferences(cancellationToken);

        var budget = Math.Max(1, options.Value.MaxCapturesPerCycle);
        var captured = 0;
        var failed = 0;
        var upToDate = 0;
        var withoutFiling = 0;
        var refused = 0;

        foreach (var (lei, issuer) in issuers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (captured + failed >= budget)
                break;
            if (!filings.TryGetValue(lei, out var candidates))
            {
                withoutFiling++;
                continue;
            }
            var filing = EsefFilingSelection.PickLatest(candidates, issuer.MarketCountryCode);
            if (filing == null)
            {
                withoutFiling++;
                continue;
            }
            var reference = EsefFilingSelection.FilingReference(filing);
            if (stored.Contains(reference))
            {
                upToDate++;
                continue;
            }
            try
            {
                if (await Capture(issuer, filing, reference, cancellationToken))
                    captured++;
                else
                    refused++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            // One issuer's unreadable report never stops the pass; the next cycle retries it, because
            // nothing was stored for it and the corpus read is the same either way.
            catch (Exception exception)
            {
                failed++;
                logger.LogWarning(
                    exception,
                    "Could not capture the European annual report {Reference} for issuer {IssuerId}.",
                    reference,
                    issuer.Id
                );
            }
        }

        logger.LogInformation(
            "ESEF report cycle complete: {Captured} captured, {Failed} failed, {Refused} refused, "
                + "{UpToDate} already current, {WithoutFiling} with no filing in the index, "
                + "{Issuers} verified issuers, {Filings} issuers matched",
            captured,
            failed,
            refused,
            upToDate,
            withoutFiling,
            issuers.Count,
            filings.Count
        );
    }

    /// <summary>
    /// The verified issuers this lane may capture for: a legal entity identifier to match the index on, and
    /// no CIK. An issuer that also files with the SEC is left to that lane, whose facts a later-filed
    /// European report would otherwise supersede on the readers' filed-date tie-break.
    /// </summary>
    private async Task<Dictionary<string, EsefCandidateIssuer>> LoadCandidateIssuers(
        CancellationToken cancellationToken
    )
    {
        var rows = await issuerRepository
            .GetCurrentDirectory()
            .Where(issuer => issuer.Cik == null && issuer.LegalEntityIdentifier != null)
            .Select(issuer => new EsefCandidateIssuer(
                issuer.Id,
                issuer.LegalEntityIdentifier,
                issuer.Presentation.Listing.MarketCountryCode,
                issuer.FiscalYearEndMonth
            ))
            .ToListAsync(cancellationToken);
        var issuers = new Dictionary<string, EsefCandidateIssuer>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            issuers[row.LegalEntityIdentifier] = row;
        }
        return issuers;
    }

    private async Task<HashSet<string>> LoadStoredReferences(CancellationToken cancellationToken)
    {
        var references = await documentRepository
            .GetAll()
            .Where(document =>
                document.DocumentType == DocumentType.EsefAnnualReport
                && document.AccessionNumber != null
            )
            .Select(document => document.AccessionNumber)
            .ToListAsync(cancellationToken);
        return new HashSet<string>(references, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Reads the whole index rather than the countries our markets sit in, because a filing's country is
    /// where the report was filed and an issuer may file outside the country it is listed in. Only rows
    /// matching an issuer we hold are kept, so the corpus is read once and carried small.
    /// </summary>
    private async Task<Dictionary<string, List<XbrlFiling>>> ReadCorpus(
        IReadOnlyCollection<string> leis,
        CancellationToken cancellationToken
    )
    {
        var wanted = new HashSet<string>(leis, StringComparer.OrdinalIgnoreCase);
        var matched = new Dictionary<string, List<XbrlFiling>>(StringComparer.OrdinalIgnoreCase);
        var read = 0;
        var total = int.MaxValue;
        for (var page = 1; page <= MaxIndexPages && read < total; page++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var response = await client.GetFilings(page, IndexPageSize, cancellationToken);
            if (response.Filings.Count == 0)
                break;
            read += response.Filings.Count;
            total = response.TotalCount > 0 ? response.TotalCount : total;
            foreach (var filing in response.Filings)
            {
                if (
                    !EsefFilingSelection.IsEsefWithLegalEntityIdentifier(filing)
                    || !wanted.Contains(filing.EntityIdentifier)
                )
                    continue;
                if (!matched.TryGetValue(filing.EntityIdentifier, out var list))
                {
                    list = [];
                    matched[filing.EntityIdentifier] = list;
                }
                list.Add(filing);
            }
        }
        logger.LogInformation(
            "Read {Read} filings of a stated {Total} from the European filing index; {Matched} issuers matched",
            read,
            total == int.MaxValue ? 0 : total,
            matched.Count
        );
        return matched;
    }

    /// <summary>
    /// Stores one filing, or refuses it for a stated reason. Returns whether a document was written.
    /// </summary>
    private async Task<bool> Capture(
        EsefCandidateIssuer candidate,
        XbrlFiling filing,
        string reference,
        CancellationToken cancellationToken
    )
    {
        var sourceUrl = filing.ReportUrl.ToString();
        if (sourceUrl.Length > MaxSourceUrlLength)
        {
            logger.LogWarning(
                "Skipping the European annual report {Reference}: its address is {Length} characters, "
                    + "past the {Limit} the document records.",
                reference,
                sourceUrl.Length,
                MaxSourceUrlLength
            );
            return false;
        }

        var issuer = await issuerRepository.Get(candidate.Id);
        if (issuer == null)
            return false;

        var report = await client.GetReport(filing.ReportUrl, cancellationToken);
        if (report.LongLength > MaxReportBytes)
        {
            logger.LogWarning(
                "Skipping the European annual report {Reference}: it is {Size} bytes, past the "
                    + "{Limit}-byte ceiling the extraction sweep parses.",
                reference,
                report.LongLength,
                MaxReportBytes
            );
            return false;
        }

        var html = System.Text.Encoding.UTF8.GetString(report);
        var content = EsefReportContent.Build(html, normalizer, converter);
        if (content.Length == 0)
        {
            logger.LogWarning(
                "The European annual report {Reference} is stored with no retrieval text: its readable "
                    + "half is {Size} characters. Its facts are unaffected.",
                reference,
                html.Length
            );
        }
        var periodEnd = filing.PeriodEnd.Value;

        // Before the document, not after. The extraction sweep selects any captured envelope and reads the
        // issuer fresh, so a document stored first could be labelled from a missing calendar; and a save
        // that then failed would leave a stored report whose issuer never gets stamped at all.
        await StampFiscalYearEnd(issuer, periodEnd);

        await documentPersistence.Save(
            issuer,
            content,
            $"{reference}.txt",
            DocumentType.EsefAnnualReport,
            // The index states when it received the report, not when the issuer filed it. It is the only
            // date the source gives beyond the period, and it is never earlier than the filing.
            DateOnly.FromDateTime(filing.AddedAt?.Date ?? periodEnd.ToDateTime(TimeOnly.MinValue)),
            periodEnd,
            sourceUrl,
            reference,
            xbrl: XbrlCaptureResult.Captured(XbrlType.InlineIxbrl, reference, report),
            // A European report has no SEC rendering of its statements, so the capture lane that fetches
            // those is told there is nothing to fetch rather than left to discover it against EDGAR.
            reportedStatements: XbrlCaptureStatus.NotPresent,
            cancellationToken: cancellationToken
        );
        return true;
    }

    /// <summary>
    /// Records the issuer's fiscal year end from the period its own annual report covers, when nothing has
    /// recorded one yet. Without it every fact this lane stores falls back to a date-derived label, which
    /// files an annual balance sheet under a quarter.
    /// </summary>
    private async Task StampFiscalYearEnd(
        Equibles.CommonStocks.Data.Models.EquityIssuer issuer,
        DateOnly periodEnd
    )
    {
        if (issuer.FiscalYearEndMonth != null)
            return;
        await identityManager.SetFiscalYearEnd(issuer, periodEnd.Month, periodEnd.Day);
    }

    internal record EsefCandidateIssuer(
        Guid Id,
        string LegalEntityIdentifier,
        string MarketCountryCode,
        int? FiscalYearEndMonth
    );
}
