using System.Collections.Concurrent;
using System.ComponentModel;

namespace Equibles.Sec.Data.Models;

[TypeConverter(typeof(DocumentTypeConverter))]
public sealed class DocumentType
{
    public string Value { get; }
    public string DisplayName { get; }

    public DocumentType(string value, string displayName = null)
    {
        Value = value;
        DisplayName = displayName ?? value;
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
            new KeyValuePair<string, DocumentType>(Other.DisplayName, Other),
        },
        StringComparer.OrdinalIgnoreCase
    );

    public static DocumentType FromValue(string value)
    {
        return AllByValue.GetValueOrDefault(value);
    }

    public static DocumentType FromDisplayName(string displayName)
    {
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
