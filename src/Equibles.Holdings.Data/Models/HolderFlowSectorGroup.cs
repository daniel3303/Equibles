using System.ComponentModel.DataAnnotations;

namespace Equibles.Holdings.Data.Models;

/// <summary>Where a traded security falls when a filer's trades are split by sector.</summary>
public enum HolderFlowSectorGroup
{
    /// <summary>An operating company in its issuer's sector.</summary>
    [Display(Name = "Sector")]
    Sector = 0,

    /// <summary>An ETF, ETN or ETV, by its listing's exchange-traded product reference.</summary>
    [Display(Name = "Exchange-traded products")]
    ExchangeTraded = 1,

    /// <summary>A security whose issuer has no sector.</summary>
    [Display(Name = "Unclassified")]
    Unclassified = 2,
}
