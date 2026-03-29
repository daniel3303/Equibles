using Microsoft.EntityFrameworkCore;

namespace Equibles.Cftc.Data.Models;

[Index(nameof(CftcContractId), nameof(ReportDate), IsUnique = true)]
[Index(nameof(ReportDate))]
public class CftcPositionReport {
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid CftcContractId { get; set; }
    public virtual CftcContract CftcContract { get; set; }

    public DateOnly ReportDate { get; set; }

    // Core positions
    public long OpenInterest { get; set; }
    public long NonCommLong { get; set; }
    public long NonCommShort { get; set; }
    public long NonCommSpreads { get; set; }
    public long CommLong { get; set; }
    public long CommShort { get; set; }
    public long TotalRptLong { get; set; }
    public long TotalRptShort { get; set; }
    public long NonRptLong { get; set; }
    public long NonRptShort { get; set; }

    // Changes
    public long? ChangeOpenInterest { get; set; }
    public long? ChangeNonCommLong { get; set; }
    public long? ChangeNonCommShort { get; set; }
    public long? ChangeCommLong { get; set; }
    public long? ChangeCommShort { get; set; }

    // Percentage of Open Interest
    public decimal? PctNonCommLong { get; set; }
    public decimal? PctNonCommShort { get; set; }
    public decimal? PctCommLong { get; set; }
    public decimal? PctCommShort { get; set; }

    // Number of Traders
    public int? TradersTotal { get; set; }
    public int? TradersNonCommLong { get; set; }
    public int? TradersNonCommShort { get; set; }
    public int? TradersCommLong { get; set; }
    public int? TradersCommShort { get; set; }

    public DateTime CreationTime { get; set; } = DateTime.UtcNow;
}
