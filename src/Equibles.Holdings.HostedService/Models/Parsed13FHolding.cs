namespace Equibles.Holdings.HostedService.Models;

/// <summary>
/// One <c>&lt;infoTable&gt;</c> row parsed from a 13F-HR information-table XML.
/// Codes (<see cref="ShareType"/>, <see cref="InvestmentDiscretion"/>,
/// <see cref="PutCall"/>) are kept as the raw SEC strings so the real-time path
/// reuses the exact same parsing helpers as the bulk-dataset path.
/// </summary>
public class Parsed13FHolding
{
    public string Cusip { get; set; }
    public string TitleOfClass { get; set; }

    /// <summary>SEC <c>sshPrnamtType</c> code: <c>SH</c> or <c>PRN</c>.</summary>
    public string ShareType { get; set; }
    public long Shares { get; set; }

    /// <summary>SEC <c>putCall</c>: <c>Put</c>, <c>Call</c>, or null/empty.</summary>
    public string PutCall { get; set; }

    /// <summary>SEC <c>investmentDiscretion</c> code: <c>SOLE</c>, <c>DFND</c>, <c>OTR</c>.</summary>
    public string InvestmentDiscretion { get; set; }

    public long VotingAuthSole { get; set; }
    public long VotingAuthShared { get; set; }
    public long VotingAuthNone { get; set; }

    /// <summary>
    /// Reference into the filing's other-manager table (sequence number), or
    /// null when the holding is reported solely by the filing manager.
    /// </summary>
    public int? OtherManagerNumber { get; set; }
}
