using System.Text;
using System.Xml.Linq;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.InsiderTrading.BusinessLogic;
using Equibles.InsiderTrading.Data.Models;
using Equibles.InsiderTrading.Repositories;
using Equibles.Integrations.Sec.Contracts;
using Equibles.Integrations.Sec.Models;
using Equibles.Media.BusinessLogic;
using Equibles.Sec.Data.Models;
using Equibles.Sec.HostedService.Contracts;
using Equibles.Yahoo.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Sec.HostedService.Services;

/// <summary>
/// Processes SEC Form 3 and Form 4 filings by parsing the ownership XML
/// into structured InsiderOwner + InsiderTransaction database records.
/// The XML→transaction parsing lives in <see cref="InsiderFilingParser"/>;
/// this processor handles fetching, owner resolution, price validity, raw-XML
/// capture, and persistence.
/// </summary>
public class InsiderTradingFilingProcessor : IFilingProcessor
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<InsiderTradingFilingProcessor> _logger;
    private readonly ErrorReporter _errorReporter;

    public InsiderTradingFilingProcessor(
        IServiceScopeFactory scopeFactory,
        ILogger<InsiderTradingFilingProcessor> logger,
        ErrorReporter errorReporter
    )
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _errorReporter = errorReporter;
    }

    public bool CanProcess(DocumentType documentType)
    {
        return documentType == DocumentType.FormFour
            || documentType == DocumentType.FormThree
            || documentType == DocumentType.FormFourA
            || documentType == DocumentType.FormThreeA;
    }

    public async Task<HashSet<string>> FilterKnownAccessions(
        IReadOnlyCollection<string> accessionNumbers
    )
    {
        if (accessionNumbers.Count == 0)
            return [];

        await using var scope = _scopeFactory.CreateAsyncScope();
        var transactionRepository =
            scope.ServiceProvider.GetRequiredService<InsiderTransactionRepository>();

        // An accession is "known" both when its own rows exist and when an
        // amendment has superseded (or claimed) it — a superseded original has
        // no rows of its own, and without the claim column every sweep would
        // re-fetch it from EDGAR forever just to re-skip it.
        //
        // Deliberately TWO single-column probes instead of one cross-column OR:
        // EF translates the OR (plus null-compensation branches for the nullable
        // columns) into a shape Postgres plans as a bitmap heap scan — ~230 ms
        // per call at the sweep's batch rate, the single largest query cost on
        // the box — while each single-column ANY probe walks its own index in
        // well under a millisecond. The union is semantically identical: feed
        // accession numbers are never null, so the dropped null-match branches
        // could never fire.
        var candidates = accessionNumbers.ToList();
        var knownByOwnRows = await transactionRepository
            .GetAll()
            .Where(t => candidates.Contains(t.AccessionNumber))
            .Select(t => new { t.AccessionNumber, t.SupersededAccessionNumber })
            .ToListAsync();
        var knownBySupersededClaim = await transactionRepository
            .GetAll()
            .Where(t =>
                t.SupersededAccessionNumber != null
                && candidates.Contains(t.SupersededAccessionNumber)
            )
            .Select(t => new { t.AccessionNumber, t.SupersededAccessionNumber })
            .ToListAsync();

        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in knownByOwnRows.Concat(knownBySupersededClaim))
        {
            result.Add(row.AccessionNumber);
            if (row.SupersededAccessionNumber != null)
                result.Add(row.SupersededAccessionNumber);
        }
        return result;
    }

    public async Task<bool> Process(FilingData filing, CommonStock companyOutContext)
    {
        // Capture IDs from the outer-scope entity to avoid leaking untracked entities into inner scope
        var companyId = companyOutContext.Id;
        var companyTicker = companyOutContext.Ticker;
        var companyCiks = new List<string> { companyOutContext.Cik };
        companyCiks.AddRange(companyOutContext.SecondaryCiks);

        await using var scope = _scopeFactory.CreateAsyncScope();
        var secEdgarClient = scope.ServiceProvider.GetRequiredService<ISecEdgarClient>();
        var ownerRepository = scope.ServiceProvider.GetRequiredService<InsiderOwnerRepository>();
        var transactionRepository =
            scope.ServiceProvider.GetRequiredService<InsiderTransactionRepository>();
        var filingRepository = scope.ServiceProvider.GetRequiredService<InsiderFilingRepository>();
        var fileManager = scope.ServiceProvider.GetRequiredService<IFileManager>();
        var dailyStockPriceRepository =
            scope.ServiceProvider.GetRequiredService<DailyStockPriceRepository>();
        var priceValidator =
            scope.ServiceProvider.GetRequiredService<InsiderTransactionPriceValidator>();

        var existing = await transactionRepository
            .GetByAccessionNumber(filing.AccessionNumber)
            .AnyAsync();
        if (existing)
            return false;

        var xmlContent = await secEdgarClient.GetDocumentContent(filing);
        if (string.IsNullOrWhiteSpace(xmlContent))
        {
            _logger.LogWarning(
                "Empty content for {Ticker} Form {Form} - {AccessionNumber}",
                companyTicker,
                filing.Form,
                filing.AccessionNumber
            );
            return false;
        }

        var root = await TryParseOwnershipRoot(xmlContent, filing, companyTicker);
        if (root == null)
            return false;

        // A Form 4 appears in the EDGAR submissions feed of every CIK it references —
        // the issuer and each reporting owner. When a tracked public company (e.g.
        // Carlyle) is itself a reporting owner on another issuer's filing (e.g. its
        // sale of Medline stock), that filing surfaces in the company's own feed.
        // Attributing it here would stamp the other issuer's trade onto this ticker,
        // so skip it: the filing is imported correctly when the real issuer's feed
        // is scraped.
        if (!IssuerMatchesCompany(root, companyCiks, filing, companyTicker))
            return false;

        var owner = await TryResolveOwner(root, ownerRepository, filing, companyTicker);
        if (owner == null)
            return false;

        var isAmendment = filing.Form.Contains("/A", StringComparison.OrdinalIgnoreCase);

        // A late-arriving original whose amendment already ingested must not
        // re-insert the rows that amendment replaced (EDGAR lists newest-first,
        // so during history sweeps the 4/A routinely lands before its Form 4).
        if (
            !isAmendment
            && await TrySkipSupersededOriginal(
                transactionRepository,
                owner,
                companyId,
                filing,
                companyTicker
            )
        )
            return false;

        var originalFilingDate = isAmendment
            ? InsiderFilingParser.ParseDateOfOriginalSubmission(root)
            : null;
        string supersededAccession = null;

        if (isAmendment && originalFilingDate.HasValue)
        {
            // Stale amendment: a NEWER amendment of the same original already
            // ingested — its rows are the current truth, so skip this one.
            // Same-day chains break the tie on accession number (SEC assigns
            // them monotonically per filer agent).
            if (
                await transactionRepository
                    .GetAmendmentsOfOriginal(owner, companyId, originalFilingDate.Value)
                    .AnyAsync(t =>
                        t.FilingDate > filing.FilingDate
                        || (
                            t.FilingDate == filing.FilingDate
                            && string.Compare(t.AccessionNumber, filing.AccessionNumber) > 0
                        )
                    )
            )
            {
                _logger.LogInformation(
                    "Skipping {Form} {AccessionNumber} for {Ticker}: a newer amendment of the same original is already ingested",
                    filing.Form,
                    filing.AccessionNumber,
                    companyTicker
                );
                return false;
            }

            supersededAccession = await SupersedeOriginal(
                transactionRepository,
                owner,
                companyId,
                filing,
                originalFilingDate.Value,
                companyTicker
            );
        }
        else if (isAmendment)
        {
            _logger.LogWarning(
                "{Form} {AccessionNumber} for {Ticker} carries no parseable dateOfOriginalSubmission; ingesting without superseding the original",
                filing.Form,
                filing.AccessionNumber,
                companyTicker
            );
        }

        // Cache the raw ownership XML so the filing can be re-parsed locally
        // when the parser changes, without re-fetching from EDGAR.
        await CaptureFilingXml(root, filing, filingRepository, fileManager);

        var transactions = InsiderFilingParser.ParseTransactions(
            root,
            owner,
            companyId,
            filing,
            isAmendment
        );

        if (transactions.Count == 0)
        {
            await SaveNoSecuritiesOwnedSentinel(
                transactionRepository,
                owner,
                companyId,
                filing,
                companyTicker,
                isAmendment,
                originalFilingDate,
                supersededAccession
            );
            return true;
        }

        await ApplyPriceValidity(
            transactions,
            companyId,
            dailyStockPriceRepository,
            priceValidator
        );

        // No in-memory dedup needed: every parsed row got a unique TransactionOrder from its
        // XML position, so the (AccessionNumber, TransactionOrder) unique index can't collide
        // within a single filing. Duplicate full-filing re-imports are stopped by the
        // GetByAccessionNumber(...).AnyAsync() check at the top of Process().
        foreach (var tx in transactions)
        {
            tx.SupersededAccessionNumber = supersededAccession;
            transactionRepository.Add(tx);
        }

        await transactionRepository.SaveChanges();

        _logger.LogInformation(
            "Imported {Count} insider transactions for {Ticker} from {Form} - {AccessionNumber}",
            transactions.Count,
            companyTicker,
            filing.Form,
            filing.AccessionNumber
        );

        return true;
    }

    // EDGAR indexes an after-17:30 submission on the next business day, so the feed
    // FilingDate of an original can trail the amendment's filer-entered
    // dateOfOriginalSubmission by a weekend (+ a holiday).
    private const int OriginalDateShiftToleranceDays = 4;

    // Whether an incoming ORIGINAL was already replaced by an ingested amendment.
    // Two signals, strongest first: an amendment that explicitly claimed this
    // accession (re-listed original), or an unresolved amendment whose
    // filer-entered original date falls within the indexing-shift window of this
    // filing date — which then claims it, so the scraper's known-accession
    // prefilter drops the original from every future sweep without a fetch.
    private async Task<bool> TrySkipSupersededOriginal(
        InsiderTransactionRepository transactionRepository,
        InsiderOwner owner,
        Guid companyId,
        FilingData filing,
        string companyTicker
    )
    {
        if (await transactionRepository.GetAmendmentsClaiming(filing.AccessionNumber).AnyAsync())
        {
            _logger.LogInformation(
                "Skipping {Form} {AccessionNumber} for {Ticker}: an amendment already claimed and superseded it",
                filing.Form,
                filing.AccessionNumber,
                companyTicker
            );
            return true;
        }

        var windowStart = filing.FilingDate.AddDays(-OriginalDateShiftToleranceDays);
        var orphans = await transactionRepository
            .GetUnresolvedAmendments(owner, companyId, windowStart, filing.FilingDate)
            .ToListAsync();
        if (orphans.Count == 0)
            return false;

        // Several unresolved amendments (of DIFFERENT originals) can sit in the
        // window; pair this original with exactly one group — the exact-date
        // match when present, else the closest original date — so a sibling
        // amendment stays unresolved for ITS original instead of being consumed
        // by the wrong one.
        var targetDate = orphans.Any(t => t.OriginalFilingDate == filing.FilingDate)
            ? filing.FilingDate
            : orphans.Max(t => t.OriginalFilingDate!.Value);
        var claimed = orphans.Where(t => t.OriginalFilingDate == targetDate).ToList();

        foreach (var row in claimed)
        {
            row.SupersededAccessionNumber = filing.AccessionNumber;
        }
        await transactionRepository.SaveChanges();

        _logger.LogInformation(
            "Skipping {Form} {AccessionNumber} for {Ticker}: claimed by the already-ingested amendment dated {OriginalDate:yyyy-MM-dd}",
            filing.Form,
            filing.AccessionNumber,
            companyTicker,
            targetDate
        );
        return true;
    }

    // Replaces what an incoming amendment restates: the original filing's rows —
    // resolved to a SINGLE accession via the filer-entered original date plus the
    // indexing-shift window — and any older amendment of the same original.
    // Returns the accession this amendment now supersedes (its own resolution, or
    // one inherited from a replaced older amendment), or null when the original
    // is not ingested (pre-MinSyncDate history, or it arrives later and is
    // claimed by TrySkipSupersededOriginal). Ambiguity (several candidate
    // accessions) deletes nothing: a visible duplicate beats silently deleting a
    // legitimate sibling filing.
    private async Task<string> SupersedeOriginal(
        InsiderTransactionRepository transactionRepository,
        InsiderOwner owner,
        Guid companyId,
        FilingData filing,
        DateOnly originalFilingDate,
        string companyTicker
    )
    {
        var windowEnd = originalFilingDate.AddDays(OriginalDateShiftToleranceDays);
        var candidates = await transactionRepository
            .GetOriginalCandidates(owner, companyId, originalFilingDate, windowEnd)
            .Select(t => new { t.AccessionNumber, t.FilingDate })
            .Distinct()
            .ToListAsync();

        var exactAccessions = candidates
            .Where(c => c.FilingDate == originalFilingDate)
            .Select(c => c.AccessionNumber)
            .Distinct()
            .ToList();
        var pool =
            exactAccessions.Count > 0
                ? exactAccessions
                : candidates.Select(c => c.AccessionNumber).Distinct().ToList();

        string resolvedAccession = null;
        if (pool.Count == 1)
        {
            resolvedAccession = pool[0];
            var originalRows = await transactionRepository
                .GetByAccessionNumber(resolvedAccession)
                .ToListAsync();
            transactionRepository.Delete(originalRows);
            _logger.LogInformation(
                "Amendment {AccessionNumber} supersedes {Count} transaction(s) of original {OriginalAccession} for {Ticker}",
                filing.AccessionNumber,
                originalRows.Count,
                resolvedAccession,
                companyTicker
            );
        }
        else if (pool.Count > 1)
        {
            _logger.LogWarning(
                "Amendment {AccessionNumber} for {Ticker} matches {Count} candidate originals around {OriginalDate:yyyy-MM-dd}; superseding none to avoid deleting a sibling filing",
                filing.AccessionNumber,
                companyTicker,
                pool.Count,
                originalFilingDate
            );
        }

        // Chained amendments: an older amendment of the same original is replaced
        // wholesale, and its resolution (which original accession it consumed or
        // claimed) is inherited so the prefilter keeps dropping that original.
        var olderAmendments = await transactionRepository
            .GetAmendmentsOfOriginal(owner, companyId, originalFilingDate)
            .Where(t =>
                t.AccessionNumber != filing.AccessionNumber
                && (
                    t.FilingDate < filing.FilingDate
                    || (
                        t.FilingDate == filing.FilingDate
                        && string.Compare(t.AccessionNumber, filing.AccessionNumber) < 0
                    )
                )
            )
            .ToListAsync();
        if (olderAmendments.Count > 0)
        {
            resolvedAccession ??= olderAmendments
                .Select(t => t.SupersededAccessionNumber)
                .FirstOrDefault(a => a != null);
            transactionRepository.Delete(olderAmendments);
            _logger.LogInformation(
                "Amendment {AccessionNumber} replaces {Count} row(s) from older amendment(s) of the same original for {Ticker}",
                filing.AccessionNumber,
                olderAmendments.Count,
                companyTicker
            );
        }

        return resolvedAccession;
    }

    // Stores the parsed ownership XML as a gzip-compressed internal File so the
    // filing can be re-parsed locally if the parser changes, without re-fetching
    // from EDGAR. root.ToString() re-serializes the already-parsed (well-formed,
    // SGML-envelope-stripped) document, so the stored payload is guaranteed
    // re-parseable. The File and InsiderFiling are added to the shared context
    // here; the caller's SaveChanges persists them alongside the transactions.
    private static async Task CaptureFilingXml(
        XElement root,
        FilingData filing,
        InsiderFilingRepository filingRepository,
        IFileManager fileManager
    )
    {
        // A filing is only processed when its transactions don't yet exist, but
        // guard against a duplicate filing row so the unique accession index
        // can't be violated by a re-run.
        var alreadyCaptured = await filingRepository
            .GetByAccessionNumber(filing.AccessionNumber)
            .AnyAsync();
        if (alreadyCaptured)
            return;

        var rawBytes = Encoding.UTF8.GetBytes(root.ToString(SaveOptions.DisableFormatting));
        var compressed = GzipCompressor.Compress(rawBytes);
        var file = await fileManager.SaveInternalFile(
            compressed,
            filing.AccessionNumber,
            "gz",
            "application/gzip"
        );

        filingRepository.Add(
            new InsiderFiling
            {
                AccessionNumber = filing.AccessionNumber,
                Content = file,
                UncompressedSize = rawBytes.Length,
                CaptureStatus = InsiderFilingCaptureStatus.Captured,
            }
        );
    }

    private async Task<XElement> TryParseOwnershipRoot(
        string xmlContent,
        FilingData filing,
        string companyTicker
    )
    {
        var sanitized = InsiderFilingParser.SanitizeXml(xmlContent);

        // Pre-XML-era ownership filings (Forms 3/4/5 before SEC mandated XML around
        // mid-2003) are PEM/SGML text with no <ownershipDocument> root, so XML parsing
        // always fails with "Data at the root level is invalid". They are unsupported
        // by design — skip them quietly instead of reporting a guaranteed error per file.
        if (!sanitized.Contains("<ownershipDocument", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug(
                "Skipping legacy non-XML ownership filing for {Ticker} - {AccessionNumber}",
                companyTicker,
                filing.AccessionNumber
            );
            return null;
        }

        XDocument doc;
        try
        {
            doc = XDocument.Parse(sanitized);
        }
        catch (System.Xml.XmlException ex)
        {
            // Many legacy ownership filings are technically <ownershipDocument> XML
            // but malformed (broken <footnote>, unescaped entities, mismatched tags).
            // These are expected, non-actionable, and historically numerous — skip
            // quietly instead of flooding the Errors table with one row per filing.
            _logger.LogDebug(
                ex,
                "Skipping malformed ownership XML for {Ticker} - {AccessionNumber}",
                companyTicker,
                filing.AccessionNumber
            );
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to parse XML for {Ticker} - {AccessionNumber}",
                companyTicker,
                filing.AccessionNumber
            );
            await _errorReporter.Report(
                ErrorSource.DocumentScraper,
                "InsiderTrading.ParseXml",
                ex.Message,
                ex.StackTrace,
                $"ticker: {companyTicker}, accession: {filing.AccessionNumber}"
            );
            return null;
        }

        var root = doc.Root;
        if (root == null)
        {
            _logger.LogWarning(
                "Parsed XML has no root element for {Ticker} - {AccessionNumber}",
                companyTicker,
                filing.AccessionNumber
            );
            return null;
        }

        return root;
    }

    // True when the filing's issuer is the company being processed (matched by primary
    // or secondary CIK, leading zeros ignored). Pre-XML-era filings have no issuer block,
    // so they fall back to trusting the feed that surfaced them rather than being dropped.
    private bool IssuerMatchesCompany(
        XElement root,
        IReadOnlyCollection<string> companyCiks,
        FilingData filing,
        string companyTicker
    )
    {
        var issuerCik = InsiderFilingParser.GetIssuerCik(root);
        if (string.IsNullOrEmpty(issuerCik))
            return true;

        var matches = companyCiks.Any(c =>
            !string.IsNullOrEmpty(c)
            && string.Equals(c.TrimStart('0'), issuerCik, StringComparison.Ordinal)
        );
        if (matches)
            return true;

        _logger.LogDebug(
            "Skipping Form {Form} {AccessionNumber} for {Ticker}: issuer CIK {IssuerCik} "
                + "differs from the company (surfaced via a reporting-owner feed)",
            filing.Form,
            filing.AccessionNumber,
            companyTicker,
            issuerCik
        );
        return false;
    }

    private async Task<InsiderOwner> TryResolveOwner(
        XElement root,
        InsiderOwnerRepository ownerRepository,
        FilingData filing,
        string companyTicker
    )
    {
        var ownerElement = root.Element("reportingOwner");
        if (ownerElement == null)
        {
            _logger.LogWarning(
                "Missing reportingOwner element for {Ticker} - {AccessionNumber}",
                companyTicker,
                filing.AccessionNumber
            );
            return null;
        }

        var ownerId = ownerElement.Element("reportingOwnerId");
        var ownerCik = ownerId?.Element("rptOwnerCik")?.Value?.Trim();
        var ownerName = ownerId?.Element("rptOwnerName")?.Value?.Trim();

        if (string.IsNullOrEmpty(ownerCik) || string.IsNullOrEmpty(ownerName))
        {
            _logger.LogWarning(
                "Missing owner CIK or name for {Ticker} - {AccessionNumber}",
                companyTicker,
                filing.AccessionNumber
            );
            return null;
        }

        return await EnsureInsiderOwnerExists(ownerRepository, ownerCik, ownerName, ownerElement);
    }

    private static async Task<InsiderOwner> EnsureInsiderOwnerExists(
        InsiderOwnerRepository ownerRepository,
        string ownerCik,
        string ownerName,
        XElement ownerElement
    )
    {
        var owner = await ownerRepository.GetByOwnerCik(ownerCik);
        if (owner != null)
            return owner;

        var ownerAddress = ownerElement.Element("reportingOwnerAddress");
        var ownerRelationship = ownerElement.Element("reportingOwnerRelationship");

        owner = new InsiderOwner
        {
            OwnerCik = ownerCik,
            Name = ownerName,
            City = ownerAddress?.Element("rptOwnerCity")?.Value?.Trim(),
            StateOrCountry = ownerAddress?.Element("rptOwnerStateOrCountry")?.Value?.Trim(),
            IsDirector = InsiderFilingParser.ParseBool(
                ownerRelationship?.Element("isDirector")?.Value
            ),
            IsOfficer = InsiderFilingParser.ParseBool(
                ownerRelationship?.Element("isOfficer")?.Value
            ),
            OfficerTitle = ownerRelationship?.Element("officerTitle")?.Value?.Trim(),
            IsTenPercentOwner = InsiderFilingParser.ParseBool(
                ownerRelationship?.Element("isTenPercentOwner")?.Value
            ),
        };

        ownerRepository.Add(owner);
        await ownerRepository.SaveChanges();
        return owner;
    }

    // Form 3 with noSecuritiesOwned — save a 0-shares record so the accession-number
    // short-circuit at the top of Process() prevents re-fetching this filing every cycle.
    private async Task SaveNoSecuritiesOwnedSentinel(
        InsiderTransactionRepository transactionRepository,
        InsiderOwner owner,
        Guid companyId,
        FilingData filing,
        string companyTicker,
        bool isAmendment = false,
        DateOnly? originalFilingDate = null,
        string supersededAccessionNumber = null
    )
    {
        _logger.LogDebug(
            "No transactions found for {Ticker} - {AccessionNumber}, saving 0-shares holding",
            companyTicker,
            filing.AccessionNumber
        );

        transactionRepository.Add(
            new InsiderTransaction
            {
                InsiderOwnerId = owner.Id,
                CommonStockId = companyId,
                FilingDate = filing.FilingDate,
                TransactionDate = filing.ReportDate,
                TransactionCode = TransactionCode.Other,
                AccessionNumber = filing.AccessionNumber,
                SecurityTitle = "No Securities Owned",
                TransactionOrder = 0,
                IsAmendment = isAmendment,
                OriginalFilingDate = originalFilingDate,
                SupersededAccessionNumber = supersededAccessionNumber,
                // 0-price holding sentinel: nothing to validate or repair.
                IsPriceValid = true,
                // No security exists on a noSecuritiesOwned filing, so SecurityKind
                // stays Unknown by design — there is no table to classify it from.
                ParserVersion = InsiderTransaction.CurrentParserVersion,
            }
        );
        await transactionRepository.SaveChanges();
    }

    // Filer-reported transactionPricePerShare is unvalidated by EDGAR — some
    // filings dump the total transaction value (or a placeholder like the
    // share count) into that field, which then explodes the dashboard's
    // Shares × Price sort. Preserve the as-filed value in ReportedPricePerShare,
    // then cross-check against Yahoo's unadjusted close on the TransactionDate
    // (most recent prior trading day for weekends/holidays): plausible rows
    // stay as filed, implausible rows are repaired (total ÷ shares). If the
    // Yahoo feed hasn't caught up yet, IsPriceValid is left null (pending) —
    // the backoffice maintenance recompute re-evaluates it once the close
    // exists, rather than silently accepting it as valid.
    private static async Task ApplyPriceValidity(
        List<InsiderTransaction> transactions,
        Guid companyId,
        DailyStockPriceRepository dailyStockPriceRepository,
        InsiderTransactionPriceValidator priceValidator
    )
    {
        if (transactions.Count == 0)
            return;

        var minDate = transactions.Min(t => t.TransactionDate).AddDays(-10);
        var maxDate = transactions.Max(t => t.TransactionDate);

        var prices = await dailyStockPriceRepository
            .GetAll()
            .Where(p => p.CommonStockId == companyId && p.Date >= minDate && p.Date <= maxDate)
            .Select(p => new { p.Date, p.Close })
            .OrderByDescending(p => p.Date)
            .ToListAsync();

        foreach (var transaction in transactions)
        {
            var close = prices
                .Where(p => p.Date <= transaction.TransactionDate)
                .Select(p => (decimal?)p.Close)
                .FirstOrDefault();

            // Capture the as-filed price first; both this path and the backfill
            // manager evaluate from ReportedPricePerShare so the "reported is
            // the source of truth" invariant holds regardless of ordering.
            transaction.ReportedPricePerShare = transaction.PricePerShare;

            var evaluation = priceValidator.Evaluate(
                transaction.ReportedPricePerShare,
                transaction.Shares,
                transaction.SecurityKind,
                transaction.SecurityTitle,
                close,
                transaction.Notes
            );
            transaction.PricePerShare = evaluation.EffectivePrice;
            transaction.IsPriceValid = evaluation.IsPriceValid;
        }
    }
}
