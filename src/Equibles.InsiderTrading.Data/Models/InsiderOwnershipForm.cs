namespace Equibles.InsiderTrading.Data.Models;

/// <summary>
/// The SEC ownership-report family that produced an insider row. Derived from
/// the filing's authoritative form type, never from transaction contents.
/// </summary>
public enum InsiderOwnershipForm
{
    Unknown = 0,
    Form3 = 3,
    Form4 = 4,
    Form5 = 5,
}
