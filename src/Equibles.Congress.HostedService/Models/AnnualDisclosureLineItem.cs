using Equibles.Congress.Data.Models;

namespace Equibles.Congress.HostedService.Models;

/// <summary>
/// One asset or liability row parsed from an annual disclosure report: the
/// row's free-text description and the value range the filer checked. Rows
/// whose value column carries no dollar range ("None", "Undetermined") are
/// never materialized as line items.
/// </summary>
public class AnnualDisclosureLineItem
{
    public CongressionalDisclosureLineKind Kind { get; init; }
    public required string Description { get; init; }

    /// <summary>Lower bound of the disclosed range, in dollars.</summary>
    public long RangeMinimum { get; init; }

    /// <summary>Upper bound of the disclosed range, in dollars.</summary>
    public long RangeMaximum { get; init; }

    /// <summary>
    /// The filer's own asset-class label — a House code without its brackets
    /// ("ST") or a Senate label ("Bank Deposit"). Null on liability rows.
    /// </summary>
    public string AssetType { get; init; }

    /// <summary>The income categories the asset produced, verbatim.</summary>
    public string IncomeType { get; init; }

    /// <summary>
    /// The income bracket the asset produced, in dollars. Null when the filer
    /// disclosed no bracket; a wrapped bracket that never received its upper
    /// bound is dropped rather than halved.
    /// </summary>
    public long? IncomeMinimum { get; init; }

    /// <summary>Upper bound of the disclosed income range, in dollars.</summary>
    public long? IncomeMaximum { get; init; }
}
