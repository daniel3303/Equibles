namespace Equibles.Holdings.HostedService.Models;

/// <summary>
/// A single 13F-HR (or 13F-HR/A) submission parsed from its raw EDGAR XML
/// (<c>primary_doc.xml</c> cover page + information-table XML). This is the
/// real-time path's equivalent of one filing's worth of rows from the
/// quarterly structured data set, and is projected back into the same TSV
/// shape so it flows through the identical, already-tested import pipeline.
/// </summary>
public class Parsed13FFiling
{
    public string Cik { get; set; }
    public string AccessionNumber { get; set; }
    public DateOnly FilingDate { get; set; }
    public DateOnly PeriodOfReport { get; set; }
    public bool IsAmendment { get; set; }

    public string FilingManagerName { get; set; }
    public string City { get; set; }
    public string StateOrCountry { get; set; }
    public string Form13FFileNumber { get; set; }
    public string CrdNumber { get; set; }

    /// <summary>Other-manager table: sequence number → manager name.</summary>
    public Dictionary<int, string> OtherManagers { get; set; } = [];

    public List<Parsed13FHolding> Holdings { get; set; } = [];
}
