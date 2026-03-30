using System.ComponentModel;

namespace Equibles.Sec.Data.Models;

[TypeConverter(typeof(DocumentTypeConverter))]
public class DocumentType {
    public string Value { get; }
    public string DisplayName { get; }

    public DocumentType(string value, string displayName = null) {
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

    private static readonly Dictionary<string, DocumentType> AllByValue = new(StringComparer.OrdinalIgnoreCase) {
        [TenK.Value] = TenK, [TenQ.Value] = TenQ, [EightK.Value] = EightK,
        [TenKa.Value] = TenKa, [TenQa.Value] = TenQa, [EightKa.Value] = EightKa,
        [TwentyF.Value] = TwentyF, [SixK.Value] = SixK, [FortyF.Value] = FortyF,
        [FormFour.Value] = FormFour, [FormThree.Value] = FormThree, [Other.Value] = Other
    };

    private static readonly Dictionary<string, DocumentType> AllByDisplayName = new(StringComparer.OrdinalIgnoreCase) {
        [TenK.DisplayName] = TenK, [TenQ.DisplayName] = TenQ, [EightK.DisplayName] = EightK,
        [TenKa.DisplayName] = TenKa, [TenQa.DisplayName] = TenQa, [EightKa.DisplayName] = EightKa,
        [TwentyF.DisplayName] = TwentyF, [SixK.DisplayName] = SixK, [FortyF.DisplayName] = FortyF,
        [FormFour.DisplayName] = FormFour, [FormThree.DisplayName] = FormThree, [Other.DisplayName] = Other
    };

    public static DocumentType FromValue(string value) {
        return AllByValue.GetValueOrDefault(value);
    }

    public static DocumentType FromDisplayName(string displayName) {
        return AllByDisplayName.GetValueOrDefault(displayName);
    }

    public static IEnumerable<DocumentType> GetAll() => AllByValue.Values;

    public static void Register(DocumentType type) {
        AllByValue.TryAdd(type.Value, type);
        AllByDisplayName.TryAdd(type.DisplayName, type);
    }

    public override string ToString() => DisplayName;
    public override bool Equals(object obj) => obj is DocumentType other && Value == other.Value;
    public override int GetHashCode() => Value.GetHashCode();
    public static bool operator ==(DocumentType left, DocumentType right) => Equals(left, right);
    public static bool operator !=(DocumentType left, DocumentType right) => !Equals(left, right);
}
