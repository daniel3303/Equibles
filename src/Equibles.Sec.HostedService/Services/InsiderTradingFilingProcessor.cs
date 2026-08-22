using System.Text;
using System.Xml.Linq;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.CorporateActions.Repositories;
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
using Equibles.Sec.Repositories;
using Equibles.Yahoo.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Sec.HostedService.Services;

/// <summary>
/// Processes SEC Forms 3, 4, and 5 by parsing the ownership XML
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
            || documentType == DocumentType.FormFive
            || documentType == DocumentType.FormFourA
            || documentType == DocumentType.FormThreeA
            || documentType == DocumentType.FormFiveA;
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
        var filingRepository = scope.ServiceProvider.GetRequiredService<InsiderFilingRepository>();

        // An accession is "known" both when its own rows exist and when an
        // amendment has superseded (or claimed) it — a superseded original has
        // no rows of its own, and without the claim column every sweep would
        // re-fetch it from EDGAR forever just to re-skip it.
        //
        // Keep own-accession and superseded-claim probes separate instead of one
        // cross-column OR: Postgres plans the OR as a costly bitmap heap scan.
        // Claims additionally join the captured original's tiny, indexed filing
        // row so a legacy cross-family link cannot suppress the real accession.
        var candidates = accessionNumbers.ToList();
        var knownByOwnRows = await transactionRepository
            .GetAll()
            .Where(t => candidates.Contains(t.AccessionNumber))
            .Select(t => t.AccessionNumber)
            .Distinct()
            .ToListAsync();
        // A legacy claim is authoritative only when both the amendment row and
        // the captured original have a known, matching Form 3/4/5 family. Before
        // v8, same-day filings from different families could be linked; treating
        // that stale link as known would permanently suppress the real original.
        var knownBySupersededClaim = await (
            from transaction in transactionRepository.GetAll()
            join original in filingRepository.GetAll()
                on transaction.SupersededAccessionNumber equals original.AccessionNumber
            where
                transaction.SupersededAccessionNumber != null
                && candidates.Contains(transaction.SupersededAccessionNumber)
                && transaction.FilingForm != InsiderOwnershipForm.Unknown
                && transaction.FilingForm == original.FilingForm
            select transaction.SupersededAccessionNumber
        )
            .Distinct()
            .ToListAsync();

        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var accession in knownByOwnRows)
            result.Add(accession);
        foreach (var accession in knownBySupersededClaim)
            result.Add(accession);
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
        var stockSplitRepository = scope.ServiceProvider.GetRequiredService<StockSplitRepository>();

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

        var tombstoneRepository =
            scope.ServiceProvider.GetRequiredService<FailedFilingIngestRepository>();

        var (root, deterministicSkipReason) = await TryParseOwnershipRoot(
            xmlContent,
            filing,
            companyTicker
        );
        if (root == null)
        {
            // Only content-based verdicts (identical for every feed that
            // surfaces this accession) are tombstoned; a possibly-transient
            // parse failure retries next enumeration as before.
            if (deterministicSkipReason != null)
            {
                await FilingIngestTombstones.Record(
                    tombstoneRepository,
                    companyOutContext.Cik,
                    filing,
                    deterministicSkipReason,
                    _logger
                );
            }
            return false;
        }

        // An ownership filing appears in the EDGAR submissions feed of every CIK it references —
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
        {
            // Content-based verdict: the ownership XML itself lacks a usable
            // reporting-owner identity, regardless of which feed surfaced it.
            await FilingIngestTombstones.Record(
                tombstoneRepository,
                companyOutContext.Cik,
                filing,
                "ownership XML missing reporting-owner identity",
                _logger
            );
            return false;
        }

        var isAmendment = filing.Form.Contains("/A", StringComparison.OrdinalIgnoreCase);
        var ownershipForm = InsiderFilingParser.ParseOwnershipForm(filing.Form);
        await StampFilingForm(filingRepository, filing.AccessionNumber, ownershipForm);

        // A late-arriving original whose amendment already ingested must not
        // re-insert the rows that amendment replaced (EDGAR lists newest-first,
        // so during history sweeps the 4/A routinely lands before its Form 4).
        if (
            !isAmendment
            && await TrySkipSupersededOriginal(
                transactionRepository,
                filingRepository,
                fileManager,
                owner,
                companyId,
                filing,
                ownershipForm,
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
            // The v8 migration initializes legacy rows to Unknown. Resolve only the
            // relevant original/amendment candidates from their captured SEC XML before
            // the family-scoped queries run, or a live amendment racing the replay could
            // leave a same-family legacy row permanently duplicated.
            await ResolveLegacyAmendmentCandidates(
                transactionRepository,
                filingRepository,
                fileManager,
                owner,
                companyId,
                originalFilingDate.Value,
                _logger
            );

            // Stale amendment: a NEWER amendment of the same original already
            // ingested — its rows are the current truth, so skip this one.
            // Same-day chains break the tie on accession number (SEC assigns
            // them monotonically per filer agent).
            if (
                await transactionRepository
                    .GetAmendmentsOfOriginal(
                        owner,
                        companyId,
                        originalFilingDate.Value,
                        ownershipForm
                    )
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
                // Nothing is persisted for a stale amendment, so without a
                // tombstone every enumeration re-downloads it just to re-skip.
                // The verdict is the issuer's own data (a mismatched issuer
                // never reaches this branch), and the monthly retry
                // re-evaluates it if the newer amendment's rows ever go away.
                await FilingIngestTombstones.Record(
                    tombstoneRepository,
                    companyOutContext.Cik,
                    filing,
                    "superseded by a newer ingested amendment of the same original",
                    _logger
                );
                return false;
            }

            supersededAccession = await SupersedeOriginal(
                transactionRepository,
                filingRepository,
                owner,
                companyId,
                filing,
                originalFilingDate.Value,
                ownershipForm,
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
            await FilingIngestTombstones.Clear(
                tombstoneRepository,
                filing.AccessionNumber,
                _logger
            );
            return true;
        }

        await ApplyPriceValidity(
            transactions,
            companyId,
            companyOutContext.Ticker,
            companyOutContext.SecondaryTickers,
            dailyStockPriceRepository,
            stockSplitRepository,
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
        await FilingIngestTombstones.Clear(tombstoneRepository, filing.AccessionNumber, _logger);

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
        InsiderFilingRepository filingRepository,
        IFileManager fileManager,
        InsiderOwner owner,
        Guid companyId,
        FilingData filing,
        InsiderOwnershipForm ownershipForm,
        string companyTicker
    )
    {
        await ResolveLegacyClaimForms(
            transactionRepository,
            filingRepository,
            fileManager,
            filing.AccessionNumber,
            _logger
        );
        await PrepareLegacyOriginalCandidates(
            transactionRepository,
            filingRepository,
            fileManager,
            owner,
            companyId,
            filing.FilingDate,
            _logger
        );

        if (
            await transactionRepository
                .GetAmendmentsClaiming(filing.AccessionNumber, ownershipForm)
                .AnyAsync()
        )
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
            .GetUnresolvedAmendments(
                owner,
                companyId,
                windowStart,
                filing.FilingDate,
                ownershipForm
            )
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

    // v8 introduced FilingForm after claims already existed. Resolve an unknown
    // claimant from its captured SEC ownership XML before deciding whether it
    // suppresses an incoming original. An unreadable claimant stays unknown and
    // is ignored: a visible duplicate is safer than deleting the wrong family.
    private static async Task ResolveLegacyClaimForms(
        InsiderTransactionRepository transactionRepository,
        InsiderFilingRepository filingRepository,
        IFileManager fileManager,
        string supersededAccessionNumber,
        ILogger<InsiderTradingFilingProcessor> logger
    )
    {
        var accessions = await transactionRepository
            .GetAll()
            .Where(t =>
                t.SupersededAccessionNumber == supersededAccessionNumber
                && t.FilingForm == InsiderOwnershipForm.Unknown
            )
            .Select(t => t.AccessionNumber)
            .Distinct()
            .ToListAsync();
        await ResolveLegacyFilingForms(
            transactionRepository,
            filingRepository,
            fileManager,
            accessions,
            logger
        );
    }

    private static async Task ResolveLegacyAmendmentCandidates(
        InsiderTransactionRepository transactionRepository,
        InsiderFilingRepository filingRepository,
        IFileManager fileManager,
        InsiderOwner owner,
        Guid companyId,
        DateOnly originalFilingDate,
        ILogger<InsiderTradingFilingProcessor> logger
    )
    {
        var windowEnd = originalFilingDate.AddDays(OriginalDateShiftToleranceDays);
        var accessions = await transactionRepository
            .GetAll()
            .Where(t =>
                t.InsiderOwnerId == owner.Id
                && t.CommonStockId == companyId
                && (
                    (
                        !t.IsAmendment
                        && t.FilingDate >= originalFilingDate
                        && t.FilingDate <= windowEnd
                    ) || (t.IsAmendment && t.OriginalFilingDate == originalFilingDate)
                )
            )
            .Select(t => t.AccessionNumber)
            .Distinct()
            .ToListAsync();
        await ResolveLegacyFilingForms(
            transactionRepository,
            filingRepository,
            fileManager,
            accessions,
            logger
        );
        await ClearProvenCrossFamilyClaims(transactionRepository, filingRepository, accessions);
    }

    private static async Task PrepareLegacyOriginalCandidates(
        InsiderTransactionRepository transactionRepository,
        InsiderFilingRepository filingRepository,
        IFileManager fileManager,
        InsiderOwner owner,
        Guid companyId,
        DateOnly filingDate,
        ILogger<InsiderTradingFilingProcessor> logger
    )
    {
        var windowStart = filingDate.AddDays(-OriginalDateShiftToleranceDays);
        var accessions = await transactionRepository
            .GetAll()
            .Where(t =>
                t.InsiderOwnerId == owner.Id
                && t.CommonStockId == companyId
                && t.IsAmendment
                && t.OriginalFilingDate != null
                && t.OriginalFilingDate >= windowStart
                && t.OriginalFilingDate <= filingDate
            )
            .Select(t => t.AccessionNumber)
            .Distinct()
            .ToListAsync();
        await ResolveLegacyFilingForms(
            transactionRepository,
            filingRepository,
            fileManager,
            accessions,
            logger
        );
        await ClearProvenCrossFamilyClaims(transactionRepository, filingRepository, accessions);
    }

    // Pre-v8 same-day matching could attach an amendment to a sibling form family.
    // Clear only claims disproved by both authoritative documentType stamps; an
    // unknown target remains untouched until its cached XML can prove the mismatch.
    private static async Task ClearProvenCrossFamilyClaims(
        InsiderTransactionRepository transactionRepository,
        InsiderFilingRepository filingRepository,
        IReadOnlyCollection<string> amendmentAccessions
    )
    {
        if (amendmentAccessions.Count == 0)
            return;

        var claimedRows = await transactionRepository
            .GetAll()
            .Where(t =>
                amendmentAccessions.Contains(t.AccessionNumber)
                && t.SupersededAccessionNumber != null
                && t.FilingForm != InsiderOwnershipForm.Unknown
            )
            .ToListAsync();
        var targetAccessions = claimedRows
            .Select(t => t.SupersededAccessionNumber)
            .Distinct()
            .ToList();
        var targetFamilies = await filingRepository
            .GetAll()
            .Where(f => targetAccessions.Contains(f.AccessionNumber))
            .Select(f => new { f.AccessionNumber, f.FilingForm })
            .ToDictionaryAsync(f => f.AccessionNumber, f => f.FilingForm);

        foreach (var row in claimedRows)
        {
            if (
                targetFamilies.TryGetValue(row.SupersededAccessionNumber, out var targetFamily)
                && targetFamily != InsiderOwnershipForm.Unknown
                && targetFamily != row.FilingForm
            )
            {
                row.SupersededAccessionNumber = null;
            }
        }

        await transactionRepository.SaveChanges();
    }

    // A pre-v8 family is trusted only when it was already stamped from documentType
    // or can be recovered from the gzip-compressed SEC ownership XML. Names, dates,
    // accession shape, and transaction content never infer a form family.
    private static async Task ResolveLegacyFilingForms(
        InsiderTransactionRepository transactionRepository,
        InsiderFilingRepository filingRepository,
        IFileManager fileManager,
        IReadOnlyCollection<string> accessionNumbers,
        ILogger<InsiderTradingFilingProcessor> logger
    )
    {
        if (accessionNumbers.Count == 0)
            return;

        foreach (var accessionNumber in accessionNumbers)
        {
            var unresolved = await transactionRepository
                .GetByAccessionNumber(accessionNumber)
                .Where(t => t.FilingForm == InsiderOwnershipForm.Unknown)
                .ToListAsync();
            if (unresolved.Count == 0)
                continue;

            var stored = await filingRepository
                .GetByAccessionNumber(accessionNumber)
                .Include(f => f.Content)
                    .ThenInclude(content => content.FileContent)
                .FirstOrDefaultAsync();
            if (stored == null)
            {
                logger.LogWarning(
                    "Could not resolve legacy insider filing family for {AccessionNumber}: no cached filing row; leaving it unknown",
                    accessionNumber
                );
                continue;
            }

            var filingForm = stored.FilingForm;
            if (filingForm == InsiderOwnershipForm.Unknown)
            {
                if (
                    stored
                    is not {
                        CaptureStatus: InsiderFilingCaptureStatus.Captured,
                        ContentId: not null
                    }
                )
                {
                    logger.LogWarning(
                        "Could not resolve legacy insider filing family for {AccessionNumber}: cached XML is unavailable; leaving it unknown",
                        accessionNumber
                    );
                    continue;
                }

                try
                {
                    var compressed = await fileManager.GetContent(stored.Content);
                    if (compressed == null || compressed.Length == 0)
                    {
                        logger.LogWarning(
                            "Could not resolve legacy insider filing family for {AccessionNumber}: cached XML is empty; leaving it unknown",
                            accessionNumber
                        );
                        continue;
                    }

                    var raw = GzipCompressor.Decompress(compressed);
                    var root = InsiderFilingParser.TryGetOwnershipRoot(
                        Encoding.UTF8.GetString(raw)
                    );
                    filingForm = InsiderFilingParser.ParseOwnershipForm(
                        root?.Element("documentType")?.Value
                    );
                    if (filingForm == InsiderOwnershipForm.Unknown)
                    {
                        logger.LogWarning(
                            "Could not resolve legacy insider filing family for {AccessionNumber}: cached XML has no supported ownership documentType; leaving it unknown",
                            accessionNumber
                        );
                        continue;
                    }

                    stored.FilingForm = filingForm;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(
                        ex,
                        "Could not resolve legacy insider filing family from cached XML for {AccessionNumber}; leaving it unknown",
                        accessionNumber
                    );
                    continue;
                }
            }

            if (filingForm == InsiderOwnershipForm.Unknown)
                continue;

            foreach (var row in unresolved)
                row.FilingForm = filingForm;
        }

        await transactionRepository.SaveChanges();
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
        InsiderFilingRepository filingRepository,
        InsiderOwner owner,
        Guid companyId,
        FilingData filing,
        DateOnly originalFilingDate,
        InsiderOwnershipForm ownershipForm,
        string companyTicker
    )
    {
        var windowEnd = originalFilingDate.AddDays(OriginalDateShiftToleranceDays);
        var candidates = await transactionRepository
            .GetOriginalCandidates(owner, companyId, originalFilingDate, windowEnd, ownershipForm)
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
            .GetAmendmentsOfOriginal(owner, companyId, originalFilingDate, ownershipForm)
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
            var claimedAccessions = olderAmendments
                .Select(t => t.SupersededAccessionNumber)
                .Where(a => a != null)
                .Distinct()
                .ToList();
            var validatedClaims = await filingRepository
                .GetAll()
                .Where(f =>
                    claimedAccessions.Contains(f.AccessionNumber) && f.FilingForm == ownershipForm
                )
                .Select(f => f.AccessionNumber)
                .ToHashSetAsync();
            resolvedAccession ??= claimedAccessions.FirstOrDefault(validatedClaims.Contains);
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

    // Persist the authoritative form family before any supersession early-return.
    // The filing row survives deletion of its transaction rows, which lets the
    // known-accession prefilter validate both sides of a legacy claim.
    private static async Task StampFilingForm(
        InsiderFilingRepository filingRepository,
        string accessionNumber,
        InsiderOwnershipForm filingForm
    )
    {
        var stored = await filingRepository
            .GetByAccessionNumber(accessionNumber)
            .FirstOrDefaultAsync();
        if (stored == null)
        {
            filingRepository.Add(
                new InsiderFiling { AccessionNumber = accessionNumber, FilingForm = filingForm }
            );
        }
        else
        {
            stored.FilingForm = filingForm;
        }

        await filingRepository.SaveChanges();
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
        var stored = await filingRepository
            .GetByAccessionNumber(filing.AccessionNumber)
            .FirstOrDefaultAsync();
        if (stored is { CaptureStatus: InsiderFilingCaptureStatus.Captured, ContentId: not null })
            return;

        var rawBytes = Encoding.UTF8.GetBytes(root.ToString(SaveOptions.DisableFormatting));
        var compressed = GzipCompressor.Compress(rawBytes);
        var file = await fileManager.SaveInternalFile(
            compressed,
            filing.AccessionNumber,
            "gz",
            "application/gzip"
        );

        if (stored == null)
        {
            filingRepository.Add(
                new InsiderFiling
                {
                    AccessionNumber = filing.AccessionNumber,
                    FilingForm = InsiderFilingParser.ParseOwnershipForm(filing.Form),
                    Content = file,
                    UncompressedSize = rawBytes.Length,
                    CaptureStatus = InsiderFilingCaptureStatus.Captured,
                }
            );
        }
        else
        {
            stored.FilingForm = InsiderFilingParser.ParseOwnershipForm(filing.Form);
            stored.Content = file;
            stored.UncompressedSize = rawBytes.Length;
            stored.CaptureStatus = InsiderFilingCaptureStatus.Captured;
        }
    }

    // Returns the parsed ownership root, or (null, reason) when the content is
    // deterministically unparseable — the reason marks it safe to tombstone
    // (the verdict is identical for every feed that surfaces the accession).
    // A possibly-transient failure returns (null, null): retry next enumeration.
    private async Task<(XElement Root, string DeterministicSkipReason)> TryParseOwnershipRoot(
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
            return (null, "legacy non-XML ownership filing (pre-2003, unsupported by design)");
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
            return (null, $"malformed ownership XML: {ex.Message}");
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
                ex,
                $"ticker: {companyTicker}, accession: {filing.AccessionNumber}"
            );
            return (null, null);
        }

        var root = doc.Root;
        if (root == null)
        {
            _logger.LogWarning(
                "Parsed XML has no root element for {Ticker} - {AccessionNumber}",
                companyTicker,
                filing.AccessionNumber
            );
            return (null, null);
        }

        return (root, null);
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
                FilingForm = InsiderFilingParser.ParseOwnershipForm(filing.Form),
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
    // then cross-check against the stored close on the TransactionDate (most
    // recent prior trading day for weekends/holidays). The stored close is on
    // TODAY'S split-adjusted basis while the filed price is on the transaction
    // date's basis, so the evaluation carries the split factor and checks both
    // bases: plausible rows stay as filed, implausible rows are repaired
    // (total ÷ shares) only inside the session's price band. If the price feed
    // hasn't caught up yet — or the split basis is unsettled — IsPriceValid is
    // left null (pending) and re-evaluated later, rather than silently
    // accepted or guessed at.
    private static async Task ApplyPriceValidity(
        List<InsiderTransaction> transactions,
        Guid companyId,
        string primaryTicker,
        IReadOnlyCollection<string> secondaryTickers,
        DailyStockPriceRepository dailyStockPriceRepository,
        StockSplitRepository stockSplitRepository,
        InsiderTransactionPriceValidator priceValidator
    )
    {
        if (transactions.Count == 0)
            return;

        var minDate = transactions.Min(t => t.TransactionDate).AddDays(-10);
        var maxDate = transactions.Max(t => t.TransactionDate);

        var prices = await dailyStockPriceRepository
            .GetAll()
            .Where(p =>
                p.CommonStockId == companyId
                && p.Date >= minDate
                && p.Date <= maxDate
                && p.Volume > 0
            )
            .Select(p => new
            {
                p.Date,
                p.Close,
                p.Low,
                p.High,
            })
            .OrderByDescending(p => p.Date)
            .ToListAsync();

        var splits = await stockSplitRepository
            .GetEffectiveByStock(companyId, DateOnly.FromDateTime(DateTime.UtcNow))
            .ToListAsync();

        foreach (var transaction in transactions)
        {
            var barRow = prices.FirstOrDefault(p => p.Date <= transaction.TransactionDate);
            var bar = InsiderDailyBars.Build(
                barRow?.Close,
                barRow?.Low,
                barRow?.High,
                transaction.TransactionDate,
                splits,
                primaryTicker,
                secondaryTickers
            );

            // Capture the as-filed price first; both this path and the backfill
            // manager evaluate from ReportedPricePerShare so the "reported is
            // the source of truth" invariant holds regardless of ordering.
            transaction.ReportedPricePerShare = transaction.PricePerShare;

            var evaluation = priceValidator.Evaluate(
                transaction.ReportedPricePerShare,
                transaction.Shares,
                transaction.SecurityKind,
                transaction.SecurityTitle,
                bar,
                transaction.Notes
            );
            transaction.PricePerShare = evaluation.EffectivePrice;
            transaction.IsPriceValid = evaluation.IsPriceValid;
            transaction.PriceWasRepaired = evaluation.WasRepaired;
        }
    }
}
