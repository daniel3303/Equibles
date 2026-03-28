using System.IO.Compression;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.Holdings.HostedService.Models;

public class ImportContext {
    // Immutable inputs
    public TsvParser TsvParser { get; init; }
    public ZipArchive Archive { get; init; }
    public DateOnly MinReportDate { get; init; }
    public bool DataSetValueInThousands { get; init; }

    // Populated by phases
    public Dictionary<string, SubmissionRow> Submissions { get; set; }
    public Dictionary<string, CoverPageRow> CoverPages { get; set; }
    public Dictionary<string, Guid> CusipMapping { get; set; }
    public Dictionary<string, Guid> CikToHolderId { get; set; }

    // Other managers: AccessionNumber → (SequenceNumber → ManagerName)
    public Dictionary<string, Dictionary<int, string>> OtherManagers { get; set; } = [];

    // Consensus price cache: (CommonStockId, ReportDate) → median price (null = not enough data)
    public Dictionary<(Guid, DateOnly), decimal?> ConsensusCache { get; } = [];

    // Raw price consensus: (CommonStockId, ReportDate) → median raw VALUE/SHARES from the dataset.
    // Used for pre-2023 data to detect filers who report VALUE in dollars instead of thousands.
    public Dictionary<(Guid, DateOnly), decimal> RawPriceConsensus { get; set; } = [];
}
