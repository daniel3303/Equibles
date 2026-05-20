using System.Text.RegularExpressions;
using System.Xml.Linq;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.InsiderTrading.Data.Models;
using Equibles.InsiderTrading.Repositories;
using Equibles.Integrations.Sec.Contracts;
using Equibles.Integrations.Sec.Models;
using Equibles.Sec.Data.Models;
using Equibles.Sec.HostedService.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Sec.HostedService.Services;

/// <summary>
/// Processes SEC Form 3 and Form 4 filings by parsing the ownership XML
/// into structured InsiderOwner + InsiderTransaction database records.
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

        // Check if already imported by accession number
        var existing = await transactionRepository
            .GetByAccessionNumber(filing.AccessionNumber)
            .AnyAsync();
        if (existing)
            return false;

        // Fetch the XML document from SEC
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

        var sanitized = SanitizeXml(xmlContent);

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
            return false;
        }

        // Parse XML
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
            return false;
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
            return false;
        }

        var root = doc.Root;
        if (root == null)
        {
            _logger.LogWarning(
                "Parsed XML has no root element for {Ticker} - {AccessionNumber}",
                companyTicker,
                filing.AccessionNumber
            );
            return false;
        }

        // Extract reporting owner
        var ownerElement = root.Element("reportingOwner");
        if (ownerElement == null)
        {
            _logger.LogWarning(
                "Missing reportingOwner element for {Ticker} - {AccessionNumber}",
                companyOutContext.Ticker,
                filing.AccessionNumber
            );
            return false;
        }

        var ownerId = ownerElement.Element("reportingOwnerId");
        var ownerCik = ownerId?.Element("rptOwnerCik")?.Value?.Trim();
        var ownerName = ownerId?.Element("rptOwnerName")?.Value?.Trim();

        if (string.IsNullOrEmpty(ownerCik) || string.IsNullOrEmpty(ownerName))
        {
            _logger.LogWarning(
                "Missing owner CIK or name for {Ticker} - {AccessionNumber}",
                companyOutContext.Ticker,
                filing.AccessionNumber
            );
            return false;
        }

        // Upsert insider owner
        var owner = await ownerRepository.GetByOwnerCik(ownerCik);
        if (owner == null)
        {
            var ownerAddress = ownerElement.Element("reportingOwnerAddress");
            var ownerRelationship = ownerElement.Element("reportingOwnerRelationship");

            owner = new InsiderOwner
            {
                OwnerCik = ownerCik,
                Name = ownerName,
                City = ownerAddress?.Element("rptOwnerCity")?.Value?.Trim(),
                StateOrCountry = ownerAddress?.Element("rptOwnerStateOrCountry")?.Value?.Trim(),
                IsDirector = ParseBool(ownerRelationship?.Element("isDirector")?.Value),
                IsOfficer = ParseBool(ownerRelationship?.Element("isOfficer")?.Value),
                OfficerTitle = ownerRelationship?.Element("officerTitle")?.Value?.Trim(),
                IsTenPercentOwner = ParseBool(
                    ownerRelationship?.Element("isTenPercentOwner")?.Value
                ),
            };

            ownerRepository.Add(owner);
            await ownerRepository.SaveChanges();
        }

        var isAmendment = filing.Form.Contains("/A", StringComparison.OrdinalIgnoreCase);

        // Both non-derivative and derivative XML tables share the same element names for the
        // fields we extract (securityTitle, transactionDate, transactionCoding, transactionAmounts,
        // postTransactionAmounts, ownershipNature). The SecurityTitle distinguishes the instrument
        // type (e.g., "Common Stock" vs "Stock Option (Right to Buy)"). For derivatives, Shares
        // and PricePerShare refer to the derivative instrument, not the underlying security.
        //
        // TransactionOrder is the 0-based position of the row within its filing — assigned as
        // we parse so the (AccessionNumber, TransactionOrder) unique index has a stable key.
        // The XML's document order is the only natural identity Form 4 transactions have.
        var transactions = new List<InsiderTransaction>();

        void AddParsed(InsiderTransaction tx)
        {
            if (tx == null)
                return;
            tx.TransactionOrder = transactions.Count;
            transactions.Add(tx);
        }

        void WalkTable(string tableName, string txName, string holdingName)
        {
            var table = root.Element(tableName);
            if (table == null)
                return;
            foreach (var txElement in table.Elements(txName))
                AddParsed(ParseTransaction(txElement, owner, companyId, filing, isAmendment));
            foreach (var holdingElement in table.Elements(holdingName))
                AddParsed(ParseHolding(holdingElement, owner, companyId, filing, isAmendment));
        }

        WalkTable("nonDerivativeTable", "nonDerivativeTransaction", "nonDerivativeHolding");
        WalkTable("derivativeTable", "derivativeTransaction", "derivativeHolding");

        if (transactions.Count == 0)
        {
            // Form 3 with noSecuritiesOwned — save a 0-shares record so the accession-number
            // short-circuit at the top of Process() prevents re-fetching this filing every cycle.
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
                }
            );
            await transactionRepository.SaveChanges();

            return true;
        }

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

    private InsiderTransaction ParseTransaction(
        XElement txElement,
        InsiderOwner owner,
        Guid companyId,
        FilingData filing,
        bool isAmendment
    )
    {
        var securityTitle = GetWrappedValue(txElement, "securityTitle")?.Trim();
        var transactionDateStr = GetWrappedValue(txElement, "transactionDate");
        var codeStr = txElement
            .Element("transactionCoding")
            ?.Element("transactionCode")
            ?.Value?.Trim();
        var sharesStr = GetWrappedValue(txElement, "transactionAmounts", "transactionShares");
        var priceStr = GetWrappedValue(txElement, "transactionAmounts", "transactionPricePerShare");
        var adCode = GetWrappedValue(
            txElement,
            "transactionAmounts",
            "transactionAcquiredDisposedCode"
        )
            ?.Trim();
        var sharesAfterStr = GetWrappedValue(
            txElement,
            "postTransactionAmounts",
            "sharesOwnedFollowingTransaction"
        );
        var ownership = GetWrappedValue(txElement, "ownershipNature", "directOrIndirectOwnership")
            ?.Trim();

        // Form 4 transactionDate is ISO yyyy-MM-dd (ownership XSD). Parse it
        // culture-independently — under a non-Gregorian host culture (e.g.
        // ar-SA Umm al-Qura) culture-sensitive TryParse fails and every
        // insider transaction would be silently dropped.
        if (
            !DateOnly.TryParse(
                transactionDateStr,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out var transactionDate
            )
        )
            return null;

        return new InsiderTransaction
        {
            InsiderOwnerId = owner.Id,
            CommonStockId = companyId,
            FilingDate = filing.FilingDate,
            TransactionDate = transactionDate,
            TransactionCode = ParseTransactionCode(codeStr),
            Shares = ParseLong(sharesStr),
            PricePerShare = ParseDecimal(priceStr),
            AcquiredDisposed =
                adCode == "D" ? AcquiredDisposed.Disposed : AcquiredDisposed.Acquired,
            SharesOwnedAfter = ParseLong(sharesAfterStr),
            OwnershipNature = ownership == "I" ? OwnershipNature.Indirect : OwnershipNature.Direct,
            SecurityTitle = securityTitle,
            AccessionNumber = filing.AccessionNumber,
            IsAmendment = isAmendment,
        };
    }

    private InsiderTransaction ParseHolding(
        XElement holdingElement,
        InsiderOwner owner,
        Guid companyId,
        FilingData filing,
        bool isAmendment
    )
    {
        var securityTitle = GetWrappedValue(holdingElement, "securityTitle")?.Trim();
        var sharesStr = GetWrappedValue(
            holdingElement,
            "postTransactionAmounts",
            "sharesOwnedFollowingTransaction"
        );
        var ownership = GetWrappedValue(
            holdingElement,
            "ownershipNature",
            "directOrIndirectOwnership"
        )
            ?.Trim();

        return new InsiderTransaction
        {
            InsiderOwnerId = owner.Id,
            CommonStockId = companyId,
            FilingDate = filing.FilingDate,
            TransactionDate = filing.ReportDate,
            TransactionCode = TransactionCode.Other,
            Shares = ParseLong(sharesStr),
            PricePerShare = 0,
            AcquiredDisposed = AcquiredDisposed.Acquired,
            SharesOwnedAfter = ParseLong(sharesStr),
            OwnershipNature = ownership == "I" ? OwnershipNature.Indirect : OwnershipNature.Direct,
            SecurityTitle = securityTitle,
            AccessionNumber = filing.AccessionNumber,
            IsAmendment = isAmendment,
        };
    }

    // SEC ownership XML wraps each field in <field><value>...</value></field>,
    // sometimes nested under a grouping element (transactionAmounts, etc.).
    // Walk the path then read the inner <value>.
    private static string GetWrappedValue(XElement parent, params string[] path)
    {
        var element = parent;
        foreach (var name in path)
        {
            element = element?.Element(name);
        }
        return element?.Element("value")?.Value;
    }

    internal static string SanitizeXml(string xml)
    {
        // SEC filings wrap the actual XML inside an SGML envelope.
        // Extract the XML content from within <XML>...</XML> tags.
        var xmlStart = xml.IndexOf("<XML>", StringComparison.OrdinalIgnoreCase);
        var xmlEnd = xml.IndexOf("</XML>", StringComparison.OrdinalIgnoreCase);
        if (xmlStart >= 0 && xmlEnd > xmlStart)
        {
            xml = xml[(xmlStart + 5)..xmlEnd].Trim();
        }

        // Fix unescaped ampersands in entity names
        return Regex.Replace(xml, @"&(?!(amp|lt|gt|quot|apos|#\d+|#x[\da-fA-F]+);)", "&amp;");
    }

    internal static TransactionCode ParseTransactionCode(string code)
    {
        return code?.ToUpperInvariant() switch
        {
            "P" => TransactionCode.Purchase,
            "S" => TransactionCode.Sale,
            "A" => TransactionCode.Award,
            "M" => TransactionCode.Conversion,
            "X" => TransactionCode.Exercise,
            "F" => TransactionCode.TaxPayment,
            "E" => TransactionCode.Expiration,
            "G" => TransactionCode.Gift,
            "I" => TransactionCode.Inheritance,
            "W" => TransactionCode.Discretionary,
            _ => TransactionCode.Other,
        };
    }

    internal static bool ParseBool(string value)
    {
        return value is "1" or "true" or "True" or "TRUE";
    }

    internal static long ParseLong(string value)
    {
        if (string.IsNullOrEmpty(value))
            return 0;
        return long.TryParse(value, out var result) ? result : (long)ParseDecimal(value);
    }

    internal static decimal ParseDecimal(string value)
    {
        if (string.IsNullOrEmpty(value))
            return 0;
        return decimal.TryParse(
            value,
            System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture,
            out var result
        )
            ? result
            : 0;
    }
}
