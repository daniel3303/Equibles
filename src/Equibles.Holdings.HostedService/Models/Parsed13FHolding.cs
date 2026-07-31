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

    /// <summary>
    /// SEC <c>value</c>: the position's market value exactly as filed (whole
    /// dollars for filings on/after 2023-01-03, thousands before). Carried so
    /// the import pipeline can cross-check it against the share count.
    /// </summary>
    public long Value { get; set; }

    /// <summary>SEC <c>putCall</c>: <c>Put</c>, <c>Call</c>, or null/empty.</summary>
    public string PutCall { get; set; }

    /// <summary>SEC <c>investmentDiscretion</c> code: <c>SOLE</c>, <c>DFND</c>, <c>OTR</c>.</summary>
    public string InvestmentDiscretion { get; set; }

    public long VotingAuthSole { get; set; }
    public long VotingAuthShared { get; set; }
    public long VotingAuthNone { get; set; }

    /// <summary>
    /// The raw OTHERMANAGER attribution as filed — a comma-separated list of summary-page
    /// sequence numbers ("4,8,11"), one number, or empty when the filing manager reports the
    /// holding alone. Kept raw so the synthetic archive round-trips the full list and the bulk
    /// reader applies the one shared interpretation; plucking the first number here would
    /// silently discard the shared-attribution information for realtime filings only.
    /// </summary>
    public string OtherManagers { get; set; }
}
