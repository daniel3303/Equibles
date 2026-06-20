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
        var footnotes = BuildFootnotes(root);

        // The Rule 10b5-1 affirmative-defense checkbox is a single document-level
        // element, so it applies to every row this filing contributes — stamp it
        // on each as they are added.
        var rule10b5One = ParseRule10b5One(root);

        void AddParsed(InsiderTransaction tx)
        {
            if (tx == null)
                return;
            tx.TransactionOrder = transactions.Count;
            tx.IsRule10b5One = rule10b5One;
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
                AddParsed(
                    ParseTransaction(
                        txElement,
                        owner,
                        companyId,
                        filing,
                        isAmendment,
                        kind,
                        footnotes
                    )
                );
            foreach (var holdingElement in table.Elements(holdingName))
                AddParsed(
                    ParseHolding(
                        holdingElement,
                        owner,
                        companyId,
                        filing,
                        isAmendment,
                        kind,
                        footnotes
                    )
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

        // A Form 3 with <noSecuritiesOwned>1</noSecuritiesOwned> reports that the owner
        // holds none of the issuer's securities — a legitimate filing whose tables are
        // empty by design. Emit a single 0-shares sentinel so this parse (the shared
        // source of truth) yields the same one row the ingest pipeline stores. Without
        // it the reprocess pipeline, which replays this parse, re-derives zero rows and
        // logs a phantom "stored 1 but re-parsed 0" divergence for every such filing.
        if (transactions.Count == 0 && DeclaresNoSecuritiesOwned(root))
            AddParsed(BuildNoSecuritiesOwnedSentinel(owner, companyId, filing));

        return transactions;
    }

    // Form 3 initial statements set <noSecuritiesOwned> to 1 when the reporting owner
    // holds none of the issuer's securities; both ownership tables are then empty.
    internal static bool DeclaresNoSecuritiesOwned(XElement root) =>
        ParseBool(root.Element("noSecuritiesOwned")?.Value);

    // The Rule 10b5-1(c) affirmative-defense checkbox (<aff10b5One>), added to the
    // ownership schema in EDGAR 23.1 (2023). A direct value element on the document
    // root — not wrapped in <value> like the transaction fields. Tri-state: absent
    // (pre-2023 schema) returns null so "unknown" stays distinct from an explicit
    // unchecked box (0 → false).
    internal static bool? ParseRule10b5One(XElement root)
    {
        var element = root.Element("aff10b5One");
        return element == null ? null : ParseBool(element.Value);
    }

    // The 0-shares row recorded for a noSecuritiesOwned Form 3 — the owner's zero
    // baseline. No security exists to classify, so SecurityKind stays Unknown; the
    // 0 price is valid by design (nothing to validate or repair). Mirrors the row the
    // ingest pipeline persists so a re-parse reproduces it exactly.
    internal static InsiderTransaction BuildNoSecuritiesOwnedSentinel(
        InsiderOwner owner,
        Guid companyId,
        FilingData filing
    ) =>
        new()
        {
            InsiderOwnerId = owner.Id,
            CommonStockId = companyId,
            FilingDate = filing.FilingDate,
            TransactionDate = filing.ReportDate,
            TransactionCode = TransactionCode.Other,
            AccessionNumber = filing.AccessionNumber,
            SecurityTitle = "No Securities Owned",
            IsPriceValid = true,
            ParserVersion = InsiderTransaction.CurrentParserVersion,
        };

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

    /// <summary>
    /// The issuer CIK declared in the ownership document, with leading zeros stripped
    /// so it compares against the un-padded CIK stored on the company. Returns null for
    /// pre-XML-era filings that have no issuer block. A Form 4 surfaces in the EDGAR feed
    /// of every CIK it references — issuer and each reporting owner — so this lets callers
    /// confirm a filing actually belongs to the company being processed rather than to a
    /// public-company insider that merely reported a trade in another issuer.
    /// </summary>
    public static string GetIssuerCik(XElement root)
    {
        var cik = root?.Element("issuer")?.Element("issuerCik")?.Value?.Trim();
        return string.IsNullOrEmpty(cik) ? null : cik.TrimStart('0');
    }

    internal static InsiderTransaction ParseTransaction(
        XElement txElement,
        InsiderOwner owner,
        Guid companyId,
        FilingData filing,
        bool isAmendment,
        InsiderSecurityKind kind,
        IReadOnlyDictionary<string, string> footnotes
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

        // A Form 4 reports an already-executed trade and must be filed within two business
        // days of it, so the transaction can never post-date its filing. Filer year typos (a
        // date keyed 2035 or 0022) otherwise pass through verbatim and sort to the top or
        // bottom of the insider history. Anchor an implausible date to the filing's period of
        // report — the date SEC requires to match the transaction, and the same anchor the
        // holding path already trusts.
        if (!IsPlausibleTransactionDate(transactionDate, filing.FilingDate))
            transactionDate = filing.ReportDate;

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
            Notes = ExtractNotes(txElement, footnotes),
        };
    }

    internal static InsiderTransaction ParseHolding(
        XElement holdingElement,
        InsiderOwner owner,
        Guid companyId,
        FilingData filing,
        bool isAmendment,
        InsiderSecurityKind kind,
        IReadOnlyDictionary<string, string> footnotes
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
            // A holding element reports a position, not a trade: tag it Holding so
            // transaction lists can drop it while ownership summaries keep it.
            TransactionCode = TransactionCode.Holding,
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
            Notes = ExtractNotes(holdingElement, footnotes),
        };
    }

    // Build the filing's footnote table: id → text. The <footnotes> block sits at
    // the document root; transactions reference its entries by id. Returns an empty
    // map when the filing has no footnotes.
    private static Dictionary<string, string> BuildFootnotes(XElement root)
    {
        var footnotesElement = root.Element("footnotes");
        if (footnotesElement == null)
            return new Dictionary<string, string>();

        var result = new Dictionary<string, string>();
        foreach (var footnote in footnotesElement.Elements("footnote"))
        {
            var id = footnote.Attribute("id")?.Value;
            var text = footnote.Value?.Trim();
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(text))
                continue;
            // Last one wins on a duplicate id (shouldn't happen, but be deterministic).
            result[id] = text;
        }
        return result;
    }

    // Resolve every footnote referenced anywhere within a transaction/holding
    // element — Form 4 places <footnoteId id="Fx"/> on the row itself and on
    // individual fields (price, shares, ownership). Collect them in document
    // order, de-duplicated by id, and map to their text. Form 4 transactions are
    // flat (siblings under their table), so this subtree holds only this row's
    // references — never a sibling row's or the document-level <footnotes> block.
    internal static List<string> ExtractNotes(
        XElement element,
        IReadOnlyDictionary<string, string> footnotes
    )
    {
        var notes = new List<string>();
        var seen = new HashSet<string>();
        foreach (var reference in element.Descendants("footnoteId"))
        {
            var id = reference.Attribute("id")?.Value;
            if (string.IsNullOrEmpty(id) || !seen.Add(id))
                continue;
            if (footnotes.TryGetValue(id, out var text) && !string.IsNullOrEmpty(text))
                notes.Add(text);
        }
        return notes;
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

    // SEC's electronic ownership filings describe modern trades; a year before this floor
    // (the earliest seen in production was 0022) is a keyed-wrong date, not a real trade.
    // Paired with the "never after the filing date" rule it brackets a plausible transaction
    // date in both directions.
    internal const int MinPlausibleTransactionYear = 1900;

    internal static bool IsPlausibleTransactionDate(
        DateOnly transactionDate,
        DateOnly filingDate
    ) => transactionDate.Year >= MinPlausibleTransactionYear && transactionDate <= filingDate;

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
