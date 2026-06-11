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
}
