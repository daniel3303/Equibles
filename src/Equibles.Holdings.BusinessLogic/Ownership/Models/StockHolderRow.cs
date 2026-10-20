using Equibles.Holdings.Data.Models;

namespace Equibles.Holdings.BusinessLogic.Ownership.Models;

/// <summary>
/// One holder's position in a listing from one disclosure. Shares are restated onto today's split
/// basis from the row's own as-of date; the filed figures stay beside them.
/// </summary>
public sealed class StockHolderRow
{
    public StockHolderKey HolderKey { get; init; }
    public StockHolderSource Source { get; init; }
    public string Name { get; init; }

    /// <summary>The canonical CIK of a 13F, 13D, 13G or insider filer; null for a fund series.</summary>
    public string Cik { get; init; }

    public Guid? InstitutionalHolderId { get; init; }
    public string SeriesId { get; init; }

    /// <summary>The 13F filer whose report holds a fund series' positions (<c>FundSeriesHolderLink</c>).</summary>
    public Guid? AdviserHolderId { get; init; }

    /// <summary>The CIKs a 13D names for its reporting persons; a 13G names persons without one.</summary>
    public IReadOnlyList<string> ReportingPersonCiks { get; init; } = [];

    /// <summary>A short role or registrant line: an insider's role, a fund's registrant.</summary>
    public string Detail { get; init; }

    public long Shares { get; set; }
    public decimal FiledShares { get; init; }

    /// <summary>The position's US dollar value as the source reports or derives it; null when none.</summary>
    public long? Value { get; init; }

    /// <summary>Restated shares against the same source's previous filing; null when there is none to compare.</summary>
    public long? ChangeShares { get; set; }

    public DateOnly? PriorAsOf { get; set; }
    public decimal? PercentOutstanding { get; set; }
    public decimal? FiledPercentOfClass { get; init; }

    /// <summary>The position's share of the holder's book, in percent.</summary>
    public decimal? PortfolioWeight { get; set; }

    /// <summary>13F report date, 13D or 13G event date, insider transaction date or fund report period.</summary>
    public DateOnly AsOf { get; init; }

    public DateOnly? FilingDate { get; init; }
    public string Accession { get; init; }

    /// <summary>
    /// The purpose a 13D states in Item 4, as <see cref="IBeneficialOwnershipPurposeSource"/>
    /// describes it; null for other sources or without a reading.
    /// </summary>
    public string Purpose { get; set; }

    /// <summary>A 13F row carried from the previous quarter while its filer has not filed the open one.</summary>
    public bool Carried { get; init; }

    public HolderEntityType? EntityType { get; set; }
    public HolderStyleBand? StyleBand { get; set; }

    /// <summary>The EDGAR state or country code of the holder's address.</summary>
    public string Country { get; set; }

    /// <summary>The ultimate parent CIK the holder rolls up to; its own CIK when it has none.</summary>
    public string RollupCik { get; set; }

    /// <summary>
    /// The CIK of the 13F filer of this listing whose report includes this holder's shares: the
    /// holder itself, or the parent it nests under by confirmed reporting evidence; null otherwise.
    /// </summary>
    public string Covering13FCik { get; set; }

    /// <summary>The <see cref="RowKey"/> of the default-view row this row nests under.</summary>
    public string ParentRowKey { get; set; }

    public StockHolderRowFlags Flags { get; set; }

    /// <summary>The default-view rows nested under this one, largest first.</summary>
    public List<StockHolderRow> Children { get; private set; } = [];

    /// <summary>How many rows nest under this one, including any a cached copy leaves out.</summary>
    public int ChildCount { get; set; }

    /// <summary>Unique per lane and holder: every lane gives one row per holder.</summary>
    public string RowKey => $"{Source}:{HolderKey}";

    public bool Has(StockHolderRowFlags flag) => (Flags & flag) == flag;

    /// <summary>A copy carrying at most <paramref name="maxChildren"/> of its children, for a cache.</summary>
    public StockHolderRow WithFirstChildren(int maxChildren)
    {
        var copy = (StockHolderRow)MemberwiseClone();
        copy.Children = Children.Take(maxChildren).ToList();
        return copy;
    }
}
