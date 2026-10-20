using Equibles.Holdings.Data.Models;

namespace Equibles.Holdings.Repositories.Models;

/// <summary>
/// One Schedule 13D or 13G stake in an exact listed security: the lead person's position the
/// holdings row keeps, the filer, and the cover-page facts when the detail sync has read the
/// filing. The prior fields describe the same filer's previous 13D or 13G for the listing.
/// </summary>
public class Schedule13DGPosition
{
    public Guid HoldingId { get; set; }
    public Guid InstitutionalHolderId { get; set; }
    public string HolderCik { get; set; }
    public string HolderName { get; set; }
    public string HolderStateOrCountry { get; set; }
    public FilingType FilingType { get; set; }

    /// <summary>The cover-page event date, stored as the row's report date.</summary>
    public DateOnly EventDate { get; set; }

    public DateOnly FilingDate { get; set; }
    public string AccessionNumber { get; set; }
    public bool IsAmendment { get; set; }
    public long Shares { get; set; }
    public decimal? PercentOfClass { get; set; }
    public string ListedTicker { get; set; }

    /// <summary>True when the detail sync stored the filing's cover-page facts.</summary>
    public bool HasCoverPage { get; set; }

    public Schedule13GRules Rules { get; set; }
    public ReportingPersonTypes FilingPersonTypes { get; set; }

    public long? PriorShares { get; set; }
    public DateOnly? PriorEventDate { get; set; }
    public FilingType? PriorFilingType { get; set; }
}
