using System.IO.Compression;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.Holdings.HostedService.Models;

public class ImportContext
{
    // Immutable inputs
    public TsvParser TsvParser { get; init; }
    public ZipArchive Archive { get; init; }
    public DateOnly MinReportDate { get; init; }

    // Populated by phases
    public Dictionary<string, SubmissionRow> Submissions { get; set; }
    public Dictionary<string, CoverPageRow> CoverPages { get; set; }
    public Dictionary<string, Guid> CusipMapping { get; set; }
    public Dictionary<string, Guid> CikToHolderId { get; set; }

    // Other managers: AccessionNumber → (SequenceNumber → ManagerName)
    public Dictionary<string, Dictionary<int, string>> OtherManagers { get; set; } = [];

    // For Schedule 13D/13G submissions only: AccessionNumber → tracked stock ids
    // of the issuer(s) the filing reports. A 13D/G covers a single issuer, so its
    // amendment delete must be scoped to that issuer rather than the holder's
    // whole (reportDate, filingType) slice.
    public Dictionary<string, HashSet<Guid>> ScheduleAccessionStockIds { get; set; } = [];

    // Yahoo stock prices: (CommonStockId, ReportDate) → closing price
    public Dictionary<(Guid, DateOnly), decimal> StockPrices { get; set; } = [];
}
