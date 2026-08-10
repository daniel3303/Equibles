using Equibles.CorporateActions.Data.Models;

namespace Equibles.CorporateActions.Data;

/// <summary>
/// Bounds a raw-price comparison to one authoritative split interval.
/// </summary>
/// <remarks>
/// Stored raw bars carry no row-level split-basis metadata, and a completed provider refresh does
/// not prove which basis the provider returned. When a requested window crosses a captured split,
/// only bars on or after the latest split are guaranteed to share one interval without classifying
/// price movements heuristically.
/// </remarks>
public readonly record struct ComparablePriceWindow(
    DateOnly RequestedStart,
    DateOnly Start,
    DateOnly End,
    DateOnly? SplitBoundaryDate
)
{
    public bool IsSplitLimited => SplitBoundaryDate != null;

    public static ComparablePriceWindow Resolve(
        DateOnly requestedStart,
        DateOnly end,
        IEnumerable<StockSplit> applicableSplits
    )
    {
        if (requestedStart > end)
            throw new ArgumentOutOfRangeException(
                nameof(requestedStart),
                "The requested price-window start must not be after its end."
            );

        var boundary = (applicableSplits ?? [])
            .Where(split => split.EffectiveDate > requestedStart && split.EffectiveDate <= end)
            .Select(split => (DateOnly?)split.EffectiveDate)
            .Max();

        return new ComparablePriceWindow(requestedStart, boundary ?? requestedStart, end, boundary);
    }
}
