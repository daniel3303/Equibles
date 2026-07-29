using System.IO.Compression;
using Equibles.CorporateActions.Data.Models;
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

    // Issuer size: CommonStockId → (shares outstanding, market capitalization). Used only to
    // reject a position larger than the company it is in (ImpossiblePositionGuard); a stock
    // missing here is simply not judged.
    public Dictionary<Guid, IssuerSize> IssuerSizes { get; set; } = [];

    // Splits per stock, needed to restate an as-filed share count onto the basis the stored price
    // series uses before the two are multiplied (see HoldingValueBasis). A stock missing here has
    // never split, so its count and price already share a basis.
    public Dictionary<Guid, List<StockSplit>> StockSplits { get; set; } = [];

    // Tally of derived-vs-filed value disagreements across this import. Advisory only — it changes
    // nothing that is stored, it just makes a units error loud instead of silent.
    public ValueBasisAudit ValueBasisAudit { get; } = new();

    // Positions the import had to leave out because their CUSIP matches no tracked stock,
    // accumulated per (CUSIP, quarter) so the whole data set is counted before anything is written.
    public Dictionary<
        (string Cusip, DateOnly ReportDate),
        UnmappedCusipTally
    > UnmappedCusips { get; } = [];

    // The filer's own declared totals per accession (summary-page tableEntryTotal /
    // tableValueTotal, value already era-normalised to dollars). Carried onto the filing rollup
    // so surfaces can state what the filing declares beside what this platform tracks.
    public Dictionary<string, (int? EntryTotal, long? ValueTotal)> SummaryPages { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    // The filing whose unmapped CUSIPs are currently being collected, and the CUSIPs already
    // counted for it, so a position split across otherManager legs counts once (see
    // HoldingsImportService.RecordUnmappedCusip). Reset at each accession boundary.
    public string UnmappedCusipAccession { get; set; }

    public HashSet<string> UnmappedCusipsSeenInAccession { get; } = [];
}
