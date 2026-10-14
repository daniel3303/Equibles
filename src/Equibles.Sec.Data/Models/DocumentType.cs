using System.Collections.Concurrent;
using System.ComponentModel;

namespace Equibles.Sec.Data.Models;

[TypeConverter(typeof(DocumentTypeConverter))]
public sealed class DocumentType
{
    public string Value { get; }
    public string DisplayName { get; }

    /// <summary>
    /// When true, documents of this type are excluded from unfiltered document listings
    /// (filings lists and filing-type pickers) — they still surface through search and can
    /// be listed by requesting the type explicitly. Meant for registered types that are not
    /// SEC filings (e.g. investor-relations news), which would otherwise crowd real filings
    /// out of "recent documents" lists. Defaults to false, so registering a type without
    /// setting it changes nothing.
    /// </summary>
    public bool HiddenFromFilingLists { get; }

    public DocumentType(string value, string displayName = null, bool hiddenFromFilingLists = false)
    {
        Value = value;
        DisplayName = displayName ?? value;
        HiddenFromFilingLists = hiddenFromFilingLists;
    }

    public static readonly DocumentType TenK = new("TenK", "10-K");
    public static readonly DocumentType TenQ = new("TenQ", "10-Q");
    public static readonly DocumentType EightK = new("EightK", "8-K");
    public static readonly DocumentType TenKa = new("TenKa", "10-K/A");
    public static readonly DocumentType TenQa = new("TenQa", "10-Q/A");
    public static readonly DocumentType EightKa = new("EightKa", "8-K/A");
    public static readonly DocumentType TwentyF = new("TwentyF", "20-F");
    public static readonly DocumentType SixK = new("SixK", "6-K");
    public static readonly DocumentType FortyF = new("FortyF", "40-F");
    public static readonly DocumentType TwentyFa = new("TwentyFa", "20-F/A");
    public static readonly DocumentType SixKa = new("SixKa", "6-K/A");
    public static readonly DocumentType FortyFa = new("FortyFa", "40-F/A");
    public static readonly DocumentType FormFour = new("FormFour", "4");
    public static readonly DocumentType FormThree = new("FormThree", "3");
    public static readonly DocumentType FormFive = new("FormFive", "5");
    public static readonly DocumentType FormFourA = new("FormFourA", "4/A");
    public static readonly DocumentType FormThreeA = new("FormThreeA", "3/A");
    public static readonly DocumentType FormFiveA = new("FormFiveA", "5/A");
    public static readonly DocumentType Form144 = new("Form144", "144");
    public static readonly DocumentType FormD = new("FormD", "D");
    public static readonly DocumentType FormDa = new("FormDa", "D/A");
    public static readonly DocumentType NCen = new("NCen", "N-CEN");
    public static readonly DocumentType NCenA = new("NCenA", "N-CEN/A");
    public static readonly DocumentType NportP = new("NportP", "NPORT-P");
    public static readonly DocumentType NportPa = new("NportPa", "NPORT-P/A");
    public static readonly DocumentType Def14A = new("Def14A", "DEF 14A");
    public static readonly DocumentType Defa14A = new("Defa14A", "DEFA14A");
    public static readonly DocumentType Defm14A = new("Defm14A", "DEFM14A");
    public static readonly DocumentType Defr14A = new("Defr14A", "DEFR14A");
    public static readonly DocumentType Pre14A = new("Pre14A", "PRE 14A");
    public static readonly DocumentType Prem14A = new("Prem14A", "PREM14A");
    public static readonly DocumentType SOne = new("SOne", "S-1");
    public static readonly DocumentType SOneA = new("SOneA", "S-1/A");
    public static readonly DocumentType SThree = new("SThree", "S-3");
    public static readonly DocumentType SThreeA = new("SThreeA", "S-3/A");
    public static readonly DocumentType SFour = new("SFour", "S-4");
    public static readonly DocumentType SFourA = new("SFourA", "S-4/A");
    public static readonly DocumentType FOne = new("FOne", "F-1");
    public static readonly DocumentType FOneA = new("FOneA", "F-1/A");
    public static readonly DocumentType FThree = new("FThree", "F-3");
    public static readonly DocumentType FThreeA = new("FThreeA", "F-3/A");
    public static readonly DocumentType FFour = new("FFour", "F-4");
    public static readonly DocumentType FFourA = new("FFourA", "F-4/A");
    public static readonly DocumentType TenderOfferThirdParty = new("TenderOfferThirdParty", "SC TO-T");
    public static readonly DocumentType TenderOfferThirdPartyA = new("TenderOfferThirdPartyA", "SC TO-T/A");
    public static readonly DocumentType TenderOfferIssuer = new("TenderOfferIssuer", "SC TO-I");
    public static readonly DocumentType TenderOfferIssuerA = new("TenderOfferIssuerA", "SC TO-I/A");
    public static readonly DocumentType TenderOfferCommunication = new("TenderOfferCommunication", "SC TO-C");
    public static readonly DocumentType TenderOfferResponse = new("TenderOfferResponse", "SC 14D9");
    public static readonly DocumentType TenderOfferResponseA = new("TenderOfferResponseA", "SC 14D9/A");
    public static readonly DocumentType LateTenK = new("LateTenK", "NT 10-K");
    public static readonly DocumentType LateTenQ = new("LateTenQ", "NT 10-Q");
    public static readonly DocumentType LateTwentyF = new("LateTwentyF", "NT 20-F");
    public static readonly DocumentType AnnualReportToHolders = new("AnnualReportToHolders", "ARS");

    // The annual financial report a European issuer files under the ESEF regulation. It is not an SEC filing,
    // so it stays out of unfiltered filing lists the way registered non-filing types do.
    public static readonly DocumentType EsefAnnualReport = new(
        "EsefAnnualReport",
        "ESEF Annual Report",
        hiddenFromFilingLists: true
    );

    // ESEF identifies a reporting format, not an annual reporting period.
    public static readonly DocumentType EsefReport = new(
        "EsefReport",
        "ESEF Report",
        hiddenFromFilingLists: true
    );

    public bool IsEsef() => this == EsefAnnualReport || this == EsefReport;

    public static readonly DocumentType Other = new("Other", "Other");

    // Every built-in type; commercial modules add theirs through Register.
    private static readonly DocumentType[] BuiltIn =
    [
        TenK,
        TenQ,
        EightK,
        TenKa,
        TenQa,
        EightKa,
        TwentyF,
        SixK,
        FortyF,
        TwentyFa,
        SixKa,
        FortyFa,
        FormFour,
        FormThree,
        FormFive,
        FormFourA,
        FormThreeA,
        FormFiveA,
        Form144,
        FormD,
        FormDa,
        NCen,
        NCenA,
        NportP,
        NportPa,
        Def14A,
        Defa14A,
        Defm14A,
        Defr14A,
        Pre14A,
        Prem14A,
        SOne,
        SOneA,
        SThree,
        SThreeA,
        SFour,
        SFourA,
        FOne,
        FOneA,
        FThree,
        FThreeA,
        FFour,
        FFourA,
        TenderOfferThirdParty,
        TenderOfferThirdPartyA,
        TenderOfferIssuer,
        TenderOfferIssuerA,
        TenderOfferCommunication,
        TenderOfferResponse,
        TenderOfferResponseA,
        LateTenK,
        LateTenQ,
        LateTwentyF,
        AnnualReportToHolders,
        EsefAnnualReport,
        EsefReport,
        Other,
    ];

    private static readonly ConcurrentDictionary<string, DocumentType> AllByValue = new(
        BuiltIn.Select(t => KeyValuePair.Create(t.Value, t)),
        StringComparer.OrdinalIgnoreCase
    );

    private static readonly ConcurrentDictionary<string, DocumentType> AllByDisplayName = new(
        BuiltIn.Select(t => KeyValuePair.Create(t.DisplayName, t)),
        StringComparer.OrdinalIgnoreCase
    );

    public static DocumentType FromValue(string value)
    {
        // The OrdinalIgnoreCase comparer's GetHashCode throws on null, so a
        // null lookup key must short-circuit. FromValue is a lookup that
        // returns null on no match — the EF value converter relies on this
        // for NULL columns (DocumentType.FromValue(v) ?? new DocumentType(v)).
        if (value == null)
        {
            return null;
        }

        return AllByValue.GetValueOrDefault(value);
    }

    public static DocumentType FromDisplayName(string displayName)
    {
        if (displayName == null)
        {
            return null;
        }

        return AllByDisplayName.GetValueOrDefault(displayName);
    }

    public static IEnumerable<DocumentType> GetAll() => AllByValue.Values;

    public static void Register(DocumentType type)
    {
        AllByValue.TryAdd(type.Value, type);
        AllByDisplayName.TryAdd(type.DisplayName, type);
    }

    public override string ToString() => DisplayName;

    public override bool Equals(object obj) => obj is DocumentType other && Value == other.Value;

    public override int GetHashCode() => Value.GetHashCode();

    public static bool operator ==(DocumentType left, DocumentType right) => Equals(left, right);

    public static bool operator !=(DocumentType left, DocumentType right) => !Equals(left, right);
}
