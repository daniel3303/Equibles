using System.ComponentModel.DataAnnotations;

namespace Equibles.InsiderTrading.Data.Models;

public enum TransactionCode
{
    [Display(Name = "Purchase")]
    Purchase,

    [Display(Name = "Sale")]
    Sale,

    [Display(Name = "Award")]
    Award,

    [Display(Name = "Conversion")]
    Conversion,

    [Display(Name = "Exercise")]
    Exercise,

    [Display(Name = "Tax Payment")]
    TaxPayment,

    [Display(Name = "Expiration")]
    Expiration,

    [Display(Name = "Gift")]
    Gift,

    [Display(Name = "Inheritance")]
    Inheritance,

    [Display(Name = "Discretionary")]
    Discretionary,

    [Display(Name = "Other")]
    Other,

    /// <summary>
    /// Not a transaction: a position snapshot parsed from a Form 3/4/5
    /// <c>nonDerivativeHolding</c>/<c>derivativeHolding</c> element. The filing
    /// reports the holding with no trade, so <see cref="InsiderTransaction.Shares"/>
    /// carries the whole position (equal to
    /// <see cref="InsiderTransaction.SharesOwnedAfter"/>) and the price is 0.
    /// Kept so ownership summaries can read the position, but excluded from
    /// transaction lists — see <c>ExcludeHoldings</c>.
    /// </summary>
    [Display(Name = "Holding")]
    Holding,
}
