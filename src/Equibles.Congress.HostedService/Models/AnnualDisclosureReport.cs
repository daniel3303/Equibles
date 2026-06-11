using Equibles.Congress.Data.Models;

namespace Equibles.Congress.HostedService.Models;

/// <summary>
/// One electronically-filed annual financial disclosure report (House Form A /
/// Senate eFD annual report), parsed to the disclosed grain: the filer's
/// identity fields as the source publishes them plus the asset and liability
/// rows with their checked value ranges. Member resolution and the net-worth
/// band rollup happen at ingestion.
/// </summary>
public class AnnualDisclosureReport
{
    public required string MemberName { get; init; }
    public CongressPosition Position { get; init; }
    public string StateDistrict { get; init; }

    /// <summary>The calendar year the report covers (not the filing year).</summary>
    public int Year { get; init; }

    public DateOnly FiledDate { get; init; }

    /// <summary>House Clerk DocID / Senate eFD report id.</summary>
    public required string ReportId { get; init; }

    /// <summary>
    /// True when the source marks the report as an amendment to a previously
    /// filed annual report; the latest filed report replaces earlier ones.
    /// </summary>
    public bool IsAmendment { get; init; }

    public List<AnnualDisclosureLineItem> Lines { get; init; } = [];
}
