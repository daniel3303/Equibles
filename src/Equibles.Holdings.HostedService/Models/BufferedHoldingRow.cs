using Equibles.Holdings.Data.Models;

namespace Equibles.Holdings.HostedService.Models;

/// <summary>
/// One INFOTABLE row parsed for the current filing, buffered until the
/// accession boundary so per-filing sanity checks (the duplicated share-count
/// column repair) can see the whole filing before rows merge into upsert
/// batches.
/// </summary>
public class BufferedHoldingRow
{
    public InstitutionalHolding Holding { get; set; }
    public HoldingManagerEntry ManagerEntry { get; set; }

    /// <summary>
    /// The position's market value exactly as filed (SEC <c>VALUE</c> column);
    /// 0 when the column is absent. Used only to cross-check the share count —
    /// persisted values are always derived from shares × closing price.
    /// </summary>
    public long ReportedValue { get; set; }
}
