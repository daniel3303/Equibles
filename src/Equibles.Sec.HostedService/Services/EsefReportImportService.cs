using Equibles.CommonStocks.BusinessLogic;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.AutoWiring;
using Equibles.Integrations.Common.Http;
using Equibles.Integrations.XbrlFilings;
using Equibles.Integrations.XbrlFilings.Models;
using Equibles.Sec.BusinessLogic;
using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.BusinessLogic.Parsers;
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
/// Stores European annual report history for every verified issuer we hold, as documents carrying the
/// report's own XBRL envelope. Nothing here extracts a fact: the extraction sweep selects any captured
/// envelope, so storing the document is the whole of this lane's work.
/// </summary>
[Service]
public class EsefReportImportService(
    XbrlFilingsClient client,
    EquityIssuerRepository issuerRepository,
    DocumentRepository documentRepository,
    EsefOversizedReportRepository oversizedRepository,
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
    /// The largest report this lane stores, equal to the extraction sweep's own parse ceiling: past it a
    /// report yields no fact, so storing it would only spend the issuer's one accession on bytes nothing
    /// reads. The host states no content length, so a report past the ceiling is refused only after this
    /// many bytes of it have been read; the refusal is therefore recorded in
    /// <see cref="EsefOversizedReport"/> against this number, which re-opens the filing if the ceiling
    /// ever rises. Pinned equal to the extractor's by test.
    /// </summary>
    internal const int MaxReportBytes = 50 * 1024 * 1024;

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
        var refusals = await LoadRefusals(cancellationToken);

        var budget = Math.Max(1, options.Value.MaxCapturesPerCycle);
        var captured = 0;
        var failed = 0;
        var upToDate = 0;
        var withoutFiling = 0;
        var refused = 0;
        var refusedBefore = 0;
        var skipped = 0;

        var orderedIssuers = issuers.OrderBy(pair =>
        {
            if (!filings.TryGetValue(pair.Key, out var available))
                return true;
            var latest = EsefFilingSelection.PickLatest(available, pair.Value.MarketCountryCode);
            if (latest == null)
                return true;
            var latestReference = EsefFilingSelection.FilingReference(latest);
            return stored.Contains(latestReference) || CaptureAddress(latest, refusals) == null;
        });
        foreach (var (lei, issuer) in orderedIssuers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // A refusal is charged: the host states no content length, so reaching the ceiling costs the
            // ceiling's worth of transfer. It is charged safely only because it is remembered, so one
            // report spends the budget once rather than every cycle. A skip costs no request and is free.
            if (captured + failed + refused >= budget)
                break;
            if (!filings.TryGetValue(lei, out var candidates))
            {
                withoutFiling++;
                continue;
            }
            var history = EsefFilingSelection.PickHistory(candidates, issuer.MarketCountryCode);
            if (history.Count == 0)
            {
                withoutFiling++;
                continue;
            }
            var filing = history.FirstOrDefault(candidate =>
                !stored.Contains(EsefFilingSelection.FilingReference(candidate))
                && CaptureAddress(candidate, refusals) != null
            );
            if (filing == null)
            {
                if (history.Any(candidate => CaptureAddress(candidate, refusals) == null))
                    refusedBefore++;
                else
                    upToDate++;
                continue;
            }
            var reference = EsefFilingSelection.FilingReference(filing);
            try
            {
                switch (
                    await Capture(
                        issuer,
                        filing,
                        history[0].PeriodEnd.Value,
                        reference,
                        CaptureAddress(filing, refusals),
                        cancellationToken
                    )
                )
                {
                    case EsefCaptureOutcome.Stored:
                        captured++;
                        // A report refused under a lower ceiling and stored under this one leaves a row
                        // that contradicts the document, so it is cleared rather than kept.
                        if (refusals.ContainsKey(reference))
                            await ForgetOversized(reference);
                        break;
                    case EsefCaptureOutcome.Refused:
                        refused++;
                        break;
                    case EsefCaptureOutcome.Skipped:
                        skipped++;
                        break;
                    default:
                        throw new InvalidOperationException("Unhandled capture outcome.");
                }
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
            finally
            {
                // One scoped context serves the whole pass, so a failed save leaves its rows tracked and the
                // next issuer's save would flush them outside any transaction. Each issuer starts clean.
                documentRepository.ClearChangeTracker();
            }
        }

        logger.LogInformation(
            "ESEF report cycle complete: {Captured} captured, {Failed} failed, {Refused} refused as too "
                + "large, {RefusedBefore} refused on an earlier cycle, {Skipped} skipped, "
                + "{UpToDate} already current, {WithoutFiling} with no filing in the index, "
                + "{Issuers} verified issuers, {Filings} issuers matched",
            captured,
            failed,
            refused,
            refusedBefore,
            skipped,
            upToDate,
            withoutFiling,
            issuers.Count,
            filings.Count
        );
    }

    /// <summary>
    /// The issuers this lane may capture for: a legal entity identifier to match the index on, and no CIK.
    /// The current directory also carries US listings; those are left to the SEC lane by the CIK rule, and
    /// one whose CIK is not yet resolved is admitted here but matches nothing in a European index. An issuer that also files with the SEC is left to that lane, whose facts a later-filed
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
                issuer.Presentation.Listing.MarketCountryCode
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

    private async Task<Dictionary<string, EsefOversizedReport>> LoadRefusals(
        CancellationToken cancellationToken
    )
    {
        var rows = await oversizedRepository.GetAll().AsNoTracking().ToListAsync(cancellationToken);
        return rows.ToDictionary(row => row.Reference, StringComparer.OrdinalIgnoreCase);
    }

    private static Uri CaptureAddress(
        XbrlFiling filing,
        IReadOnlyDictionary<string, EsefOversizedReport> refusals
    )
    {
        var reference = EsefFilingSelection.FilingReference(filing);
        if (!refusals.TryGetValue(reference, out var refusal))
            return filing.ReportUrl;
        var htmlRefused =
            (
                refusal.HtmlSourceUrl == filing.ReportUrl.ToString()
                && refusal.HtmlCeilingBytes >= MaxReportBytes
            )
            || (
                refusal.SourceUrl == filing.ReportUrl.ToString()
                && refusal.CeilingBytes >= MaxReportBytes
            );
        if (!htmlRefused)
            return filing.ReportUrl;
        if (
            filing.JsonUrl == null
            || filing.JsonUrl == filing.ReportUrl
            || (
                refusal.SourceUrl == filing.JsonUrl.ToString()
                && refusal.CeilingBytes >= MaxReportBytes
            )
        )
            return null;
        return filing.JsonUrl;
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
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
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
                    || filing.PeriodEnd > today
                    || (
                        filing.AddedAt.HasValue
                        && DateOnly.FromDateTime(filing.AddedAt.Value) > today
                    )
                    || (
                        filing.AddedAt.HasValue
                        && filing.PeriodEnd > DateOnly.FromDateTime(filing.AddedAt.Value)
                    )
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
    /// Stores one filing, or declines it for a stated reason.
    /// </summary>
    private async Task<EsefCaptureOutcome> Capture(
        EsefCandidateIssuer candidate,
        XbrlFiling filing,
        DateOnly latestPeriodEnd,
        string reference,
        Uri captureAddress,
        CancellationToken cancellationToken
    )
    {
        var sourceUrl = captureAddress.ToString();
        var isJson = captureAddress == filing.JsonUrl && captureAddress != filing.ReportUrl;
        if (sourceUrl.Length > MaxSourceUrlLength)
        {
            logger.LogWarning(
                "Skipping the European annual report {Reference}: its address is {Length} characters, "
                    + "past the {Limit} the document records.",
                reference,
                sourceUrl.Length,
                MaxSourceUrlLength
            );
            return EsefCaptureOutcome.Skipped;
        }

        var issuer = await issuerRepository.Get(candidate.Id);
        if (issuer == null)
        {
            logger.LogWarning(
                "Skipping the European annual report {Reference}: issuer {IssuerId} is gone.",
                reference,
                candidate.Id
            );
            return EsefCaptureOutcome.Skipped;
        }

        SameOriginPayload payload;
        try
        {
            payload = await client.GetReport(captureAddress, MaxReportBytes, cancellationToken);
        }
        // Only the ceiling, which is a property of the report rather than a fault. An off-origin address or
        // a redirect off the origin stays a failure, because it is a reason to re-verify the source.
        catch (SameOriginSizeException exception)
        {
            logger.LogWarning(
                exception,
                "Refusing the European annual report {Reference}: it is past the {Limit}-byte ceiling "
                    + "the extraction sweep parses.",
                reference,
                MaxReportBytes
            );
            await RememberOversized(reference, sourceUrl, isJson);
            return EsefCaptureOutcome.Refused;
        }

        var report = payload.Bytes;
        var html = SameOriginTextReader.Decode(payload.CharSet, report);
        byte[] content;
        if (isJson)
        {
            new JsonXbrlParser().Parse(html, candidate.LegalEntityIdentifier, filing.PeriodEnd);
            content = [];
        }
        else
        {
            content = EsefReportContent.Build(html, normalizer, converter);
        }
        if (!isJson && content.Length == 0)
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
        await StampFiscalYearEnd(issuer, latestPeriodEnd);

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
            xbrl: XbrlCaptureResult.Captured(
                isJson ? XbrlType.JsonXbrl : XbrlType.InlineIxbrl,
                reference,
                report
            ),
            // A European report has no SEC rendering of its statements, so the capture lane that fetches
            // those is told there is nothing to fetch rather than left to discover it against EDGAR.
            reportedStatements: XbrlCaptureStatus.NotPresent,
            cancellationToken: cancellationToken
        );
        return EsefCaptureOutcome.Stored;
    }

    /// <summary>
    /// Records the refusal against the ceiling it was made under, so the report is not fetched again while
    /// that ceiling stands. Written before the pass moves on, because one scoped context serves the whole
    /// pass and the next issuer clears the change tracker.
    /// </summary>
    private async Task RememberOversized(string reference, string sourceUrl, bool isJson)
    {
        var existing = await oversizedRepository.Get(reference);
        if (existing == null)
        {
            oversizedRepository.Add(
                new EsefOversizedReport
                {
                    Reference = reference,
                    SourceUrl = sourceUrl,
                    CeilingBytes = MaxReportBytes,
                    HtmlSourceUrl = isJson ? null : sourceUrl,
                    HtmlCeilingBytes = isJson ? null : MaxReportBytes,
                    RefusedAt = DateTime.UtcNow,
                }
            );
        }
        else
        {
            // Already tracked by the read above, so the change is saved without re-marking the row.
            var preserveJsonRefusal =
                !isJson
                && existing.HtmlSourceUrl != null
                && existing.SourceUrl != existing.HtmlSourceUrl;
            if (isJson && existing.HtmlSourceUrl == null)
            {
                existing.HtmlSourceUrl = existing.SourceUrl;
                existing.HtmlCeilingBytes = existing.CeilingBytes;
            }
            else if (!isJson)
            {
                existing.HtmlSourceUrl = sourceUrl;
                existing.HtmlCeilingBytes = MaxReportBytes;
            }
            if (!preserveJsonRefusal)
            {
                existing.SourceUrl = sourceUrl;
                existing.CeilingBytes = MaxReportBytes;
            }
            existing.RefusedAt = DateTime.UtcNow;
        }
        await oversizedRepository.SaveChanges();
    }

    /// <summary>
    /// Clears a refusal whose report has since been stored. The document is committed either way, so a
    /// failure here leaves an untidy row rather than making the capture count as a failure too.
    /// </summary>
    private async Task ForgetOversized(string reference)
    {
        try
        {
            var existing = await oversizedRepository.Get(reference);
            if (existing == null)
                return;
            oversizedRepository.Delete(existing);
            await oversizedRepository.SaveChanges();
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Could not clear the refusal recorded for the European annual report {Reference}.",
                reference
            );
        }
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

    internal enum EsefCaptureOutcome
    {
        /// <summary>The report was stored.</summary>
        Stored,

        /// <summary>
        /// The report was read to the ceiling and refused, which costs that many bytes of transfer; the
        /// refusal is recorded so they are not paid again.
        /// </summary>
        Refused,

        /// <summary>Nothing was fetched and nothing decided, so the next cycle looks at the filing again.</summary>
        Skipped,
    }

    internal record EsefCandidateIssuer(
        Guid Id,
        string LegalEntityIdentifier,
        string MarketCountryCode
    );
}
