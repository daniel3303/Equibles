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
        return documentType == DocumentType.FormFour || documentType == DocumentType.FormThree;
    }

    public async Task<bool> Process(FilingData filing, CommonStock companyOutContext)
    {
        // Capture IDs from the outer-scope entity to avoid leaking untracked entities into inner scope
        var companyId = companyOutContext.Id;
        var companyTicker = companyOutContext.Ticker;

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

        var owner = await TryResolveOwner(root, ownerRepository, filing, companyTicker);
        if (owner == null)
            return false;

        var isAmendment = filing.Form.Contains("/A", StringComparison.OrdinalIgnoreCase);

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
                companyTicker
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
        string companyTicker
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
