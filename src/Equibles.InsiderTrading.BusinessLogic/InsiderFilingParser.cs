using System.Text.RegularExpressions;
using System.Xml.Linq;
using Equibles.InsiderTrading.Data.Models;
using Equibles.Integrations.Sec.Models;

namespace Equibles.InsiderTrading.BusinessLogic;

/// <summary>
/// Pure parsing of SEC Form 3/4/5 ownership XML into <see cref="InsiderTransaction"/>
/// rows. The single source of truth for the parse, shared by the ingest pipeline
/// (which fetches the XML) and the reprocess pipeline (which replays a cached copy
/// when the parser version changes). Stateless — no I/O, no logging.
/// </summary>
public static class InsiderFilingParser
{
    private const string XmlEnvelopeStart = "<XML>";
    private const string XmlEnvelopeEnd = "</XML>";

    // Both non-derivative and derivative XML tables share the same element names for the
    // fields we extract (securityTitle, transactionDate, transactionCoding, transactionAmounts,
    // postTransactionAmounts, ownershipNature). The SecurityTitle distinguishes the instrument
    // type (e.g., "Common Stock" vs "Stock Option (Right to Buy)"). For derivatives, Shares
    // and PricePerShare refer to the derivative instrument, not the underlying security.
    //
    // TransactionOrder is the 0-based position of the row within its filing — assigned as
    // we parse so the (AccessionNumber, TransactionOrder) unique index has a stable key.
    // The XML's document order is the only natural identity Form 4 transactions have, and
    // the same order lets the reprocess pipeline map a re-parsed row back onto its stored row.
    public static List<InsiderTransaction> ParseTransactions(
        XElement root,
        InsiderOwner owner,
        Guid companyId,
        FilingData filing,
        bool isAmendment
    )
    {
        var transactions = new List<InsiderTransaction>();

        void AddParsed(InsiderTransaction tx)
        {
            if (tx == null)
                return;
            tx.TransactionOrder = transactions.Count;
            transactions.Add(tx);
        }

        void WalkTable(
            string tableName,
            string txName,
            string holdingName,
            InsiderSecurityKind kind
        )
        {
            var table = root.Element(tableName);
            if (table == null)
                return;
            foreach (var txElement in table.Elements(txName))
                AddParsed(ParseTransaction(txElement, owner, companyId, filing, isAmendment, kind));
            foreach (var holdingElement in table.Elements(holdingName))
                AddParsed(
                    ParseHolding(holdingElement, owner, companyId, filing, isAmendment, kind)
                );
        }

        // The table a row lives in is the authoritative security classification —
        // nonDerivativeTable holds the issuer's actual shares, derivativeTable
        // holds options/warrants/convertibles/etc.
        WalkTable(
            "nonDerivativeTable",
            "nonDerivativeTransaction",
            "nonDerivativeHolding",
            InsiderSecurityKind.NonDerivative
        );
        WalkTable(
            "derivativeTable",
            "derivativeTransaction",
            "derivativeHolding",
            InsiderSecurityKind.Derivative
        );

        return transactions;
    }

    /// <summary>
    /// Sanitize and parse an ownership filing payload into its <c>ownershipDocument</c>
    /// root, returning null for legacy non-XML or malformed filings. No logging — callers
    /// that need to report parse failures do so themselves.
    /// </summary>
    internal static XElement TryGetOwnershipRoot(string xmlContent)
    {
        if (string.IsNullOrWhiteSpace(xmlContent))
            return null;

        var sanitized = SanitizeXml(xmlContent);
        if (!sanitized.Contains("<ownershipDocument", StringComparison.OrdinalIgnoreCase))
            return null;

        try
        {
            return XDocument.Parse(sanitized).Root;
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
    }

    internal static InsiderTransaction ParseTransaction(
        XElement txElement,
        InsiderOwner owner,
        Guid companyId,
        FilingData filing,
        bool isAmendment,
        InsiderSecurityKind kind
    )
    {
        string Wrapped(params string[] path) => GetWrappedValue(txElement, path);

        var securityTitle = Wrapped("securityTitle")?.Trim();
        var transactionDateStr = Wrapped("transactionDate");
        var codeStr = txElement
            .Element("transactionCoding")
            ?.Element("transactionCode")
            ?.Value?.Trim();
        var sharesStr = Wrapped("transactionAmounts", "transactionShares");
        var priceStr = Wrapped("transactionAmounts", "transactionPricePerShare");
        var adCode = Wrapped("transactionAmounts", "transactionAcquiredDisposedCode")?.Trim();
        var sharesAfterStr = Wrapped("postTransactionAmounts", "sharesOwnedFollowingTransaction");
        if (!TryParseTransactionDate(transactionDateStr, out var transactionDate))
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
            OwnershipNature = ParseOwnershipNature(txElement),
            SecurityTitle = securityTitle,
            AccessionNumber = filing.AccessionNumber,
            IsAmendment = isAmendment,
            SecurityKind = kind,
            ParserVersion = InsiderTransaction.CurrentParserVersion,
        };
    }

    internal static InsiderTransaction ParseHolding(
        XElement holdingElement,
        InsiderOwner owner,
        Guid companyId,
        FilingData filing,
        bool isAmendment,
        InsiderSecurityKind kind
    )
    {
        var securityTitle = GetWrappedValue(holdingElement, "securityTitle")?.Trim();
        var sharesStr = GetWrappedValue(
            holdingElement,
            "postTransactionAmounts",
            "sharesOwnedFollowingTransaction"
        );

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
            OwnershipNature = ParseOwnershipNature(holdingElement),
            SecurityTitle = securityTitle,
            AccessionNumber = filing.AccessionNumber,
            IsAmendment = isAmendment,
            SecurityKind = kind,
            ParserVersion = InsiderTransaction.CurrentParserVersion,
        };
    }

    // "I" → Indirect, anything else (including "D" and absent) → Direct. The
    // ownershipNature element is required by the ownership XSD, but legacy
    // filings sometimes omit or misspell it; defaulting to Direct matches the
    // existing inline behavior across ParseTransaction / ParseHolding.
    internal static OwnershipNature ParseOwnershipNature(XElement element)
    {
        var value = GetWrappedValue(element, "ownershipNature", "directOrIndirectOwnership")
            ?.Trim();
        return value == "I" ? OwnershipNature.Indirect : OwnershipNature.Direct;
    }

    // SEC ownership XML wraps each field in <field><value>...</value></field>,
    // sometimes nested under a grouping element (transactionAmounts, etc.).
    // Walk the path then read the inner <value>.
    internal static string GetWrappedValue(XElement parent, params string[] path)
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
        var xmlStart = xml.IndexOf(XmlEnvelopeStart, StringComparison.OrdinalIgnoreCase);
        var xmlEnd = xml.IndexOf(XmlEnvelopeEnd, StringComparison.OrdinalIgnoreCase);
        if (xmlStart >= 0 && xmlEnd > xmlStart)
        {
            xml = xml[(xmlStart + XmlEnvelopeStart.Length)..xmlEnd].Trim();
        }

        // Fix unescaped ampersands in entity names
        return Regex.Replace(xml, @"&(?!(amp|lt|gt|quot|apos|#\d+|#x[\da-fA-F]+);)", "&amp;");
    }

    // Form 4 transactionDate is ISO yyyy-MM-dd (ownership XSD). Parse it
    // culture-independently — under a non-Gregorian host culture (e.g.
    // ar-SA Umm al-Qura) culture-sensitive TryParse fails and every insider
    // transaction would be silently dropped.
    internal static bool TryParseTransactionDate(
        string transactionDateStr,
        out DateOnly transactionDate
    ) =>
        DateOnly.TryParse(
            transactionDateStr,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None,
            out transactionDate
        );

    internal static TransactionCode ParseTransactionCode(string code)
    {
        return code?.Trim().ToUpperInvariant() switch
        {
            "P" => TransactionCode.Purchase,
            "S" => TransactionCode.Sale,
            "A" => TransactionCode.Award,
            "M" => TransactionCode.Conversion,
            "X" => TransactionCode.Exercise,
            "F" => TransactionCode.TaxPayment,
            "E" => TransactionCode.Expiration,
            "G" => TransactionCode.Gift,
            "I" => TransactionCode.Discretionary,
            "W" => TransactionCode.Inheritance,
            _ => TransactionCode.Other,
        };
    }

    internal static bool ParseBool(string value)
    {
        return value?.Trim() is "1" or "true" or "True" or "TRUE";
    }

    internal static long ParseLong(string value)
    {
        if (string.IsNullOrEmpty(value))
            return 0;
        if (long.TryParse(value, out var result))
            return result;
        var d = ParseDecimal(value);
        return d > long.MaxValue || d < long.MinValue ? 0 : (long)d;
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
