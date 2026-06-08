using Equibles.Holdings.Data.Models;

namespace Equibles.Holdings.HostedService.Models;

/// <summary>
/// A single Schedule 13D/13G (or 13D/A / 13G/A) submission parsed from its raw
/// EDGAR <c>primary_doc.xml</c>. Schedule 13D and 13G became machine-readable
/// XML on 2024-12-18, so only filings from that date forward can be parsed; the
/// two forms share a namespace family but use different element names, which the
/// parser reconciles. This is the beneficial-ownership equivalent of one 13F
/// filing and is projected into the same import pipeline by the realtime path.
/// </summary>
public class Parsed13DGFiling
{
    public string AccessionNumber { get; set; }
    public DateOnly FilingDate { get; set; }

    /// <summary>Raw SEC submission type, e.g. <c>SCHEDULE 13D</c> or <c>SCHEDULE 13G/A</c>.</summary>
    public string SubmissionType { get; set; }

    /// <summary><see cref="Data.Models.FilingType.Schedule13D"/> or <see cref="Data.Models.FilingType.Schedule13G"/>.</summary>
    public FilingType FilingType { get; set; }

    public bool IsAmendment { get; set; }

    /// <summary>CIK of the lead filer (the entity that submitted the schedule).</summary>
    public string FilerCik { get; set; }

    /// <summary>Date of the event that required the filing (the cover-page event date).</summary>
    public DateOnly DateOfEvent { get; set; }

    public string IssuerCik { get; set; }
    public string IssuerCusip { get; set; }
    public string IssuerName { get; set; }
    public string SecuritiesClassTitle { get; set; }

    public List<Parsed13DGReportingPerson> ReportingPersons { get; set; } = [];
}
