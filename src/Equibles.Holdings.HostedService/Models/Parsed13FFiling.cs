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
    public string AmendmentType { get; set; }

    public string FilingManagerName { get; set; }
    public string City { get; set; }
    public string StateOrCountry { get; set; }
    public string Form13FFileNumber { get; set; }
    public string CrdNumber { get; set; }
    public bool ConfidentialTreatmentRequested { get; set; }

    /// <summary>Summary-page <c>tableEntryTotal</c> — the filer's own position count. Null when
    /// the cover page carries no summary page (13F-NT).</summary>
    public int? TableEntryTotal { get; set; }

    /// <summary>Summary-page <c>tableValueTotal</c> — the filer's own total, in the unit the
    /// filing was made in (whole dollars post-2023). Null when absent.</summary>
    public long? TableValueTotal { get; set; }

    /// <summary>
    /// Summary-page other-manager table: sequence number → manager identity. The positions this
    /// filing reports on behalf of; a holding's <c>otherManager</c> element points at the sequence.
    /// </summary>
    public Dictionary<int, OtherManagerIdentity> OtherManagers { get; set; } = [];

    /// <summary>
    /// Cover-page other-manager list, in filed order: the managers who report FOR this filer. The
    /// opposite edge to <see cref="OtherManagers"/>, and sequence-less, so nothing points at it.
    /// </summary>
    public List<OtherManagerIdentity> CoverPageOtherManagers { get; set; } = [];

    public List<Parsed13FHolding> Holdings { get; set; } = [];
}
