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
    public static readonly DocumentType FormFour = new("FormFour", "4");
    public static readonly DocumentType FormThree = new("FormThree", "3");
    public static readonly DocumentType FormFourA = new("FormFourA", "4/A");
    public static readonly DocumentType FormThreeA = new("FormThreeA", "3/A");
    public static readonly DocumentType Form144 = new("Form144", "144");
    public static readonly DocumentType FormD = new("FormD", "D");
    public static readonly DocumentType FormDa = new("FormDa", "D/A");
    public static readonly DocumentType NCen = new("NCen", "N-CEN");
    public static readonly DocumentType NCenA = new("NCenA", "N-CEN/A");
    public static readonly DocumentType NportP = new("NportP", "NPORT-P");
    public static readonly DocumentType NportPa = new("NportPa", "NPORT-P/A");
    public static readonly DocumentType Def14A = new("Def14A", "DEF 14A");
    public static readonly DocumentType Other = new("Other", "Other");

    private static readonly ConcurrentDictionary<string, DocumentType> AllByValue = new(
        new[]
        {
            new KeyValuePair<string, DocumentType>(TenK.Value, TenK),
            new KeyValuePair<string, DocumentType>(TenQ.Value, TenQ),
            new KeyValuePair<string, DocumentType>(EightK.Value, EightK),
            new KeyValuePair<string, DocumentType>(TenKa.Value, TenKa),
            new KeyValuePair<string, DocumentType>(TenQa.Value, TenQa),
            new KeyValuePair<string, DocumentType>(EightKa.Value, EightKa),
            new KeyValuePair<string, DocumentType>(TwentyF.Value, TwentyF),
            new KeyValuePair<string, DocumentType>(SixK.Value, SixK),
            new KeyValuePair<string, DocumentType>(FortyF.Value, FortyF),
            new KeyValuePair<string, DocumentType>(FormFour.Value, FormFour),
            new KeyValuePair<string, DocumentType>(FormThree.Value, FormThree),
            new KeyValuePair<string, DocumentType>(FormFourA.Value, FormFourA),
            new KeyValuePair<string, DocumentType>(FormThreeA.Value, FormThreeA),
            new KeyValuePair<string, DocumentType>(Form144.Value, Form144),
            new KeyValuePair<string, DocumentType>(FormD.Value, FormD),
            new KeyValuePair<string, DocumentType>(FormDa.Value, FormDa),
            new KeyValuePair<string, DocumentType>(NCen.Value, NCen),
            new KeyValuePair<string, DocumentType>(NCenA.Value, NCenA),
            new KeyValuePair<string, DocumentType>(NportP.Value, NportP),
            new KeyValuePair<string, DocumentType>(NportPa.Value, NportPa),
            new KeyValuePair<string, DocumentType>(Def14A.Value, Def14A),
            new KeyValuePair<string, DocumentType>(Other.Value, Other),
        },
        StringComparer.OrdinalIgnoreCase
    );

    private static readonly ConcurrentDictionary<string, DocumentType> AllByDisplayName = new(
        new[]
        {
            new KeyValuePair<string, DocumentType>(TenK.DisplayName, TenK),
            new KeyValuePair<string, DocumentType>(TenQ.DisplayName, TenQ),
            new KeyValuePair<string, DocumentType>(EightK.DisplayName, EightK),
            new KeyValuePair<string, DocumentType>(TenKa.DisplayName, TenKa),
            new KeyValuePair<string, DocumentType>(TenQa.DisplayName, TenQa),
            new KeyValuePair<string, DocumentType>(EightKa.DisplayName, EightKa),
            new KeyValuePair<string, DocumentType>(TwentyF.DisplayName, TwentyF),
            new KeyValuePair<string, DocumentType>(SixK.DisplayName, SixK),
            new KeyValuePair<string, DocumentType>(FortyF.DisplayName, FortyF),
            new KeyValuePair<string, DocumentType>(FormFour.DisplayName, FormFour),
            new KeyValuePair<string, DocumentType>(FormThree.DisplayName, FormThree),
            new KeyValuePair<string, DocumentType>(FormFourA.DisplayName, FormFourA),
            new KeyValuePair<string, DocumentType>(FormThreeA.DisplayName, FormThreeA),
            new KeyValuePair<string, DocumentType>(Form144.DisplayName, Form144),
            new KeyValuePair<string, DocumentType>(FormD.DisplayName, FormD),
            new KeyValuePair<string, DocumentType>(FormDa.DisplayName, FormDa),
            new KeyValuePair<string, DocumentType>(NCen.DisplayName, NCen),
            new KeyValuePair<string, DocumentType>(NCenA.DisplayName, NCenA),
            new KeyValuePair<string, DocumentType>(NportP.DisplayName, NportP),
            new KeyValuePair<string, DocumentType>(NportPa.DisplayName, NportPa),
            new KeyValuePair<string, DocumentType>(Def14A.DisplayName, Def14A),
            new KeyValuePair<string, DocumentType>(Other.DisplayName, Other),
        },
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
