using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Equibles.Errors.BusinessLogic;
using Equibles.CommonStocks.Data.Models;
using Equibles.Sec.Data.Models;
using Equibles.Errors.Data.Models;
using Equibles.InsiderTrading.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.InsiderTrading.Repositories;
using Equibles.Integrations.Sec.Contracts;
using Equibles.Integrations.Sec.Models;
using Equibles.Sec.HostedService.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Sec.HostedService.Services;

/// <summary>
/// Processes SEC Form 3 and Form 4 filings by parsing the ownership XML
/// into structured InsiderOwner + InsiderTransaction database records.
/// </summary>
public class InsiderTradingFilingProcessor : IFilingProcessor {
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<InsiderTradingFilingProcessor> _logger;
    private readonly ErrorReporter _errorReporter;

    // Tracks accession numbers that were fetched but had no non-derivative data.
    // Prevents infinite re-fetching of derivative-only filings across scraper cycles.
    private readonly ConcurrentDictionary<string, byte> _emptyFilings = new();

    public InsiderTradingFilingProcessor(IServiceScopeFactory scopeFactory,
        ILogger<InsiderTradingFilingProcessor> logger,
        ErrorReporter errorReporter) {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _errorReporter = errorReporter;
    }

    public bool CanProcess(DocumentType documentType) {
        return documentType == DocumentType.FormFour || documentType == DocumentType.FormThree;
    }

    public async Task<bool> Process(FilingData filing, CommonStock companyOutContext) {
        // Capture IDs from the outer-scope entity to avoid leaking untracked entities into inner scope
        var companyId = companyOutContext.Id;
        var companyTicker = companyOutContext.Ticker;

        await using var scope = _scopeFactory.CreateAsyncScope();
        var secEdgarClient = scope.ServiceProvider.GetRequiredService<ISecEdgarClient>();
        var ownerRepository = scope.ServiceProvider.GetRequiredService<InsiderOwnerRepository>();
        var transactionRepository = scope.ServiceProvider.GetRequiredService<InsiderTransactionRepository>();

        // Check in-memory cache first (derivative-only filings that had no non-derivative data)
        if (_emptyFilings.ContainsKey(filing.AccessionNumber)) return false;

        // Check if already imported by accession number
        var existing = await transactionRepository.GetByAccessionNumber(filing.AccessionNumber).AnyAsync();
        if (existing) return false;

        // Fetch the XML document from SEC
        var xmlContent = await secEdgarClient.GetDocumentContent(filing);
        if (string.IsNullOrWhiteSpace(xmlContent)) {
            _logger.LogWarning("Empty content for {Ticker} Form {Form} - {AccessionNumber}",
                companyTicker, filing.Form, filing.AccessionNumber);
            return false;
        }

        // Parse XML
        XDocument doc;
        try {
            doc = XDocument.Parse(SanitizeXml(xmlContent));
        } catch (Exception ex) {
            _logger.LogWarning(ex, "Failed to parse XML for {Ticker} - {AccessionNumber}",
                companyTicker, filing.AccessionNumber);
            await _errorReporter.Report(ErrorSource.DocumentScraper, "InsiderTrading.ParseXml", ex.Message, ex.StackTrace, $"ticker: {companyTicker}, accession: {filing.AccessionNumber}");
            return false;
        }

        var root = doc.Root;
        if (root == null) {
            _logger.LogWarning("Parsed XML has no root element for {Ticker} - {AccessionNumber}",
                companyTicker, filing.AccessionNumber);
            return false;
        }

        // Extract reporting owner
        var ownerElement = root.Element("reportingOwner");
        if (ownerElement == null) {
            _logger.LogWarning("Missing reportingOwner element for {Ticker} - {AccessionNumber}",
                companyOutContext.Ticker, filing.AccessionNumber);
            return false;
        }

        var ownerId = ownerElement.Element("reportingOwnerId");
        var ownerCik = ownerId?.Element("rptOwnerCik")?.Value?.Trim();
        var ownerName = ownerId?.Element("rptOwnerName")?.Value?.Trim();

        if (string.IsNullOrEmpty(ownerCik) || string.IsNullOrEmpty(ownerName)) {
            _logger.LogWarning("Missing owner CIK or name for {Ticker} - {AccessionNumber}",
                companyOutContext.Ticker, filing.AccessionNumber);
            return false;
        }

        // Upsert insider owner
        var owner = await ownerRepository.GetByOwnerCik(ownerCik);
        if (owner == null) {
            var ownerAddress = ownerElement.Element("reportingOwnerAddress");
            var ownerRelationship = ownerElement.Element("reportingOwnerRelationship");

            owner = new InsiderOwner {
                OwnerCik = ownerCik,
                Name = ownerName,
                City = ownerAddress?.Element("rptOwnerCity")?.Value?.Trim(),
                StateOrCountry = ownerAddress?.Element("rptOwnerStateOrCountry")?.Value?.Trim(),
                IsDirector = ParseBool(ownerRelationship?.Element("isDirector")?.Value),
                IsOfficer = ParseBool(ownerRelationship?.Element("isOfficer")?.Value),
                OfficerTitle = ownerRelationship?.Element("officerTitle")?.Value?.Trim(),
                IsTenPercentOwner = ParseBool(ownerRelationship?.Element("isTenPercentOwner")?.Value)
            };

            ownerRepository.Add(owner);
            await ownerRepository.SaveChanges();
        }

        var isAmendment = filing.Form.Contains("/A", StringComparison.OrdinalIgnoreCase);

        // Parse non-derivative transactions
        var transactions = new List<InsiderTransaction>();
        var nonDerivTable = root.Element("nonDerivativeTable");
        if (nonDerivTable != null) {
            foreach (var txElement in nonDerivTable.Elements("nonDerivativeTransaction")) {
                var transaction = ParseNonDerivativeTransaction(txElement, owner, companyId, filing, isAmendment);
                if (transaction != null) {
                    transactions.Add(transaction);
                }
            }
        }

        // Parse non-derivative holdings (Form 3 reports initial holdings, not transactions)
        if (nonDerivTable != null) {
            foreach (var holdingElement in nonDerivTable.Elements("nonDerivativeHolding")) {
                var transaction = ParseNonDerivativeHolding(holdingElement, owner, companyId, filing, isAmendment);
                if (transaction != null) {
                    transactions.Add(transaction);
                }
            }
        }

        if (transactions.Count == 0) {
            _logger.LogDebug("No non-derivative transactions found for {Ticker} - {AccessionNumber}",
                companyTicker, filing.AccessionNumber);
            _emptyFilings.TryAdd(filing.AccessionNumber, 0);
            return false;
        }

        // Deduplicate within the batch (same composite key can appear in a single filing)
        var seen = new HashSet<(Guid, Guid, DateOnly, TransactionCode, string)>();
        var uniqueTransactions = new List<InsiderTransaction>();
        foreach (var tx in transactions) {
            var key = (tx.CommonStockId, tx.InsiderOwnerId, tx.TransactionDate, tx.TransactionCode, tx.SecurityTitle);
            if (seen.Add(key)) {
                uniqueTransactions.Add(tx);
            }
        }

        // Upsert: insert new transactions, update existing ones for amendments
        var inserted = 0;
        var updated = 0;
        foreach (var tx in uniqueTransactions) {
            var existingTx = await transactionRepository.GetByUniqueKey(
                tx.CommonStockId, tx.InsiderOwnerId, tx.TransactionDate, tx.TransactionCode, tx.SecurityTitle);

            if (existingTx == null) {
                transactionRepository.Add(tx);
                inserted++;
            } else if (isAmendment) {
                existingTx.Shares = tx.Shares;
                existingTx.PricePerShare = tx.PricePerShare;
                existingTx.AcquiredDisposed = tx.AcquiredDisposed;
                existingTx.SharesOwnedAfter = tx.SharesOwnedAfter;
                existingTx.OwnershipNature = tx.OwnershipNature;
                existingTx.FilingDate = tx.FilingDate;
                existingTx.AccessionNumber = tx.AccessionNumber;
                existingTx.IsAmendment = true;
                updated++;
            }
        }

        if (inserted == 0 && updated == 0) {
            _logger.LogDebug("All transactions already exist for {Ticker} - {AccessionNumber}",
                companyTicker, filing.AccessionNumber);
            return false;
        }

        await transactionRepository.SaveChanges();

        _logger.LogInformation("Imported {Inserted} and updated {Updated} insider transactions for {Ticker} from {Form} - {AccessionNumber}",
            inserted, updated, companyTicker, filing.Form, filing.AccessionNumber);

        return true;
    }

    private InsiderTransaction ParseNonDerivativeTransaction(XElement txElement,
        InsiderOwner owner, Guid companyId, FilingData filing, bool isAmendment) {
        var securityTitle = txElement.Element("securityTitle")?.Element("value")?.Value?.Trim();
        var transactionDateStr = txElement.Element("transactionDate")?.Element("value")?.Value;
        var codeStr = txElement.Element("transactionCoding")?.Element("transactionCode")?.Value?.Trim();
        var sharesStr = txElement.Element("transactionAmounts")?.Element("transactionShares")?.Element("value")?.Value;
        var priceStr = txElement.Element("transactionAmounts")?.Element("transactionPricePerShare")?.Element("value")?.Value;
        var adCode = txElement.Element("transactionAmounts")?.Element("transactionAcquiredDisposedCode")?.Element("value")?.Value?.Trim();
        var sharesAfterStr = txElement.Element("postTransactionAmounts")?.Element("sharesOwnedFollowingTransaction")?.Element("value")?.Value;
        var ownership = txElement.Element("ownershipNature")?.Element("directOrIndirectOwnership")?.Element("value")?.Value?.Trim();

        if (!DateOnly.TryParse(transactionDateStr, out var transactionDate)) return null;

        return new InsiderTransaction {
            InsiderOwnerId = owner.Id,
            CommonStockId = companyId,
            FilingDate = filing.FilingDate,
            TransactionDate = transactionDate,
            TransactionCode = ParseTransactionCode(codeStr),
            Shares = ParseLong(sharesStr),
            PricePerShare = ParseDecimal(priceStr),
            AcquiredDisposed = adCode == "D" ? AcquiredDisposed.Disposed : AcquiredDisposed.Acquired,
            SharesOwnedAfter = ParseLong(sharesAfterStr),
            OwnershipNature = ownership == "I" ? OwnershipNature.Indirect : OwnershipNature.Direct,
            SecurityTitle = securityTitle,
            AccessionNumber = filing.AccessionNumber,
            IsAmendment = isAmendment
        };
    }

    private InsiderTransaction ParseNonDerivativeHolding(XElement holdingElement,
        InsiderOwner owner, Guid companyId, FilingData filing, bool isAmendment) {
        var securityTitle = holdingElement.Element("securityTitle")?.Element("value")?.Value?.Trim();
        var sharesStr = holdingElement.Element("postTransactionAmounts")?.Element("sharesOwnedFollowingTransaction")?.Element("value")?.Value;
        var ownership = holdingElement.Element("ownershipNature")?.Element("directOrIndirectOwnership")?.Element("value")?.Value?.Trim();

        return new InsiderTransaction {
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
            IsAmendment = isAmendment
        };
    }

    internal static string SanitizeXml(string xml) {
        // SEC filings wrap the actual XML inside an SGML envelope.
        // Extract the XML content from within <XML>...</XML> tags.
        var xmlStart = xml.IndexOf("<XML>", StringComparison.OrdinalIgnoreCase);
        var xmlEnd = xml.IndexOf("</XML>", StringComparison.OrdinalIgnoreCase);
        if (xmlStart >= 0 && xmlEnd > xmlStart) {
            xml = xml[(xmlStart + 5)..xmlEnd].Trim();
        }

        // Fix unescaped ampersands in entity names
        return Regex.Replace(xml, @"&(?!(amp|lt|gt|quot|apos|#\d+|#x[\da-fA-F]+);)", "&amp;");
    }

    internal static TransactionCode ParseTransactionCode(string code) {
        return code?.ToUpperInvariant() switch {
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
            _ => TransactionCode.Other
        };
    }

    internal static bool ParseBool(string value) {
        return value is "1" or "true" or "True" or "TRUE";
    }

    internal static long ParseLong(string value) {
        if (string.IsNullOrEmpty(value)) return 0;
        return long.TryParse(value, out var result) ? result : (long)ParseDecimal(value);
    }

    internal static decimal ParseDecimal(string value) {
        if (string.IsNullOrEmpty(value)) return 0;
        return decimal.TryParse(value, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var result) ? result : 0;
    }

}
