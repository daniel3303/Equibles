using System.ComponentModel.DataAnnotations;

namespace Equibles.CorporateActions.Data.Models;

/// <summary>
/// Durable round-robin frontier for the capped corporate-action price reconciliation queue.
/// Advancing on selection prevents an unresolvable provider series from permanently starving
/// every pending series ordered after it.
/// </summary>
public class CorporateActionPriceReconciliationCursor
{
    public const string DefaultName = "CorporateActions.PriceReconciliation";

    [Key]
    [MaxLength(100)]
    public string Name { get; set; }

    public Guid? LastCommonStockId { get; set; }

    [MaxLength(32)]
    public string LastListedTicker { get; set; }

    public DateTime? UpdatedAt { get; set; }
}
