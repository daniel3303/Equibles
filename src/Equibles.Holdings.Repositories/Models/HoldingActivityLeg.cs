namespace Equibles.Holdings.Repositories.Models;

// One 13F row as the per-stock totals read it (see HoldingActivityLegQueries.AsActivityLegs):
// the row's identity, with its shares and value counted only when it is a common-share leg.
public class HoldingActivityLeg
{
    public Guid EquityIssuerId { get; set; }

    public Guid InstitutionalHolderId { get; set; }

    public DateOnly ReportDate { get; set; }

    public string ListedTicker { get; set; }

    // Zero on a put, call or principal row.
    public long Shares { get; set; }

    // Zero on a put, call or principal row.
    public long Value { get; set; }
}
