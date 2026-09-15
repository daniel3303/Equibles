using System.Globalization;
using System.Runtime.CompilerServices;
using System.Xml;
using System.Xml.Linq;
using Equibles.Core.Identity;
using Equibles.Integrations.Esma.Models;

namespace Equibles.Integrations.Esma;

// Streams auth.017 (full) and auth.036 (delta) reports; only element local names are matched because the
// two message families and the two authorities differ in namespace and whitespace, not in structure.
public static class FirdsRecordReader
{
    public static async IAsyncEnumerable<FirdsRecord> Read(
        Stream xml,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        var settings = new XmlReaderSettings
        {
            Async = true,
            IgnoreWhitespace = true,
            IgnoreComments = true,
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
        };
        using var reader = XmlReader.Create(xml, settings);
        while (await reader.ReadAsync())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reader.NodeType != XmlNodeType.Element)
                continue;
            var kind = reader.LocalName switch
            {
                "RefData" => FirdsRecordKind.Full,
                "NewRcrd" => FirdsRecordKind.New,
                "ModfdRcrd" => FirdsRecordKind.Modified,
                "TermntdRcrd" => FirdsRecordKind.Terminated,
                _ => (FirdsRecordKind?)null,
            };
            if (kind == null)
                continue;
            XElement element;
            using (var subtree = reader.ReadSubtree())
                element = await XElement.LoadAsync(subtree, LoadOptions.None, cancellationToken);
            yield return Map(kind.Value, element);
        }
    }

    private static FirdsRecord Map(FirdsRecordKind kind, XElement element)
    {
        var general = Child(element, "FinInstrmGnlAttrbts");
        var venue = Child(element, "TradgVnRltdAttrbts");
        var technical = Child(element, "TechAttrbts");
        var isin = Text(general, "Id");
        var cfi = Text(general, "ClssfctnTp");
        var mic = Text(venue, "Id");
        if (
            !InternationalSecurityIdentifiers.IsValidIsin(isin)
            || cfi is not { Length: 6 }
            || mic is not { Length: 4 }
        )
            throw new InvalidDataException("FIRDS record lacks a valid ISIN, CFI or venue.");
        var lei = Text(element, "Issr");
        if (lei != null && !InternationalSecurityIdentifiers.IsValidLei(lei))
            throw new InvalidDataException("FIRDS record states an invalid issuer LEI.");
        return new FirdsRecord
        {
            Kind = kind,
            Isin = isin,
            FullName = Text(general, "FullNm"),
            ShortName = Text(general, "ShrtNm"),
            Cfi = cfi,
            Currency = Text(general, "NtnlCcy"),
            Lei = lei,
            Mic = mic,
            FirstTradeDate = Instant(Text(venue, "FrstTradDt")),
            TerminationDate = Instant(Text(venue, "TermntnDt")),
            RelevantCompetentAuthority = Text(technical, "RlvntCmptntAuthrty"),
            RelevantTradingVenue = Text(technical, "RlvntTradgVn"),
        };
    }

    private static XElement Child(XElement element, string localName) =>
        element?.Elements().FirstOrDefault(child => child.Name.LocalName == localName);

    private static string Text(XElement element, string localName)
    {
        var value = Child(element, localName)?.Value?.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static DateTime? Instant(string value)
    {
        if (value == null)
            return null;
        if (
            DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var instant
            )
        )
            return instant.UtcDateTime;
        throw new InvalidDataException("FIRDS record states an unreadable date.");
    }
}
