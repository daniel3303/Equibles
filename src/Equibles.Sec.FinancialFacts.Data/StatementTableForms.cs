using Equibles.Sec.Data.Models;

namespace Equibles.Sec.FinancialFacts.Data;

/// <summary>
/// The forms a figure read from a filing's statement table is stored under, beside
/// <see cref="Enums.FinancialFactSource.ReportedStatement"/>. Each prints as the form it was filed on and is its
/// own stored value, so a read leaves the figure out on a column the covering index carries. They stay out of
/// the <see cref="DocumentType"/> registry: a fact's form is not a filing type to list or pick.
/// </summary>
public static class StatementTableForms
{
    public static readonly DocumentType SixK = new("SixKTable", DocumentType.SixK.DisplayName);
    public static readonly DocumentType SixKa = new("SixKaTable", DocumentType.SixKa.DisplayName);

    /// <summary>The form a statement-table figure of this filing is stored under.</summary>
    public static DocumentType For(DocumentType filed)
    {
        if (filed == DocumentType.SixK)
            return SixK;
        if (filed == DocumentType.SixKa)
            return SixKa;
        throw new ArgumentException($"No statement-table form for '{filed}'.", nameof(filed));
    }

    /// <summary>The form the figure's filing was filed on; any other form is its own.</summary>
    public static DocumentType Filed(DocumentType form)
    {
        if (form == SixK)
            return DocumentType.SixK;
        return form == SixKa ? DocumentType.SixKa : form;
    }

    internal static DocumentType FromStored(string value)
    {
        if (value == SixK.Value)
            return SixK;
        if (value == SixKa.Value)
            return SixKa;
        return DocumentType.FromValue(value) ?? new DocumentType(value);
    }
}
