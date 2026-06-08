namespace Equibles.Holdings.HostedService.Models;

/// <summary>
/// One reporting person (beneficial owner) parsed from a Schedule 13D/13G
/// submission. A single filing lists every member of the reporting group —
/// individual funds, their general partner, the parent, and so on — each with
/// its own beneficially-owned amount and percent of the class. Several members
/// frequently report the SAME underlying shares at different levels of the
/// ownership chain, so these are NOT additive.
/// </summary>
public class Parsed13DGReportingPerson
{
    /// <summary>
    /// The reporting person's CIK, or null when the filing marks the person as
    /// having no CIK (<c>reportingPersonNoCIK = Y</c>).
    /// </summary>
    public string Cik { get; set; }

    public string Name { get; set; }

    public long SoleVotingPower { get; set; }
    public long SharedVotingPower { get; set; }
    public long SoleDispositivePower { get; set; }
    public long SharedDispositivePower { get; set; }

    /// <summary>Aggregate amount beneficially owned by this reporting person.</summary>
    public long AggregateAmountOwned { get; set; }

    /// <summary>Percent of the class beneficially owned, or null when omitted.</summary>
    public decimal? PercentOfClass { get; set; }

    /// <summary>SEC <c>typeOfReportingPerson</c> code (e.g. <c>PN</c>, <c>IA</c>, <c>OO</c>).</summary>
    public string TypeOfReportingPerson { get; set; }

    /// <summary>SEC <c>citizenshipOrOrganization</c> code (e.g. <c>DE</c>, <c>E9</c>).</summary>
    public string CitizenshipOrOrganization { get; set; }
}
