using Equibles.Mcp.Helpers;

namespace Equibles.Sec.Mcp.Tools;

/// <summary>
/// Render-time glosses for the raw SEC enumeration codes the fund tools would otherwise leak
/// verbatim ("S-6"); the NPORT-P holding codes live in <c>NportCodes</c>. Each gloss keeps the
/// original code visible and appends a short human label. Unknown codes pass through unchanged.
/// </summary>
internal static class FundCodes
{
    /// <summary>
    /// N-CEN registrant classification: the registration form the investment company is organised
    /// under. These double as the <c>FundSeries.FundType</c> values.
    /// </summary>
    internal static string RegistrationType(string code) =>
        code switch
        {
            null or "" => "-",
            "N-1A" => "N-1A (open-end fund)",
            "N-2" => "N-2 (closed-end fund)",
            "N-3" => "N-3 (variable annuity, managed separate account)",
            "N-4" => "N-4 (variable annuity, unit investment trust)",
            "N-5" => "N-5 (small business investment company)",
            "N-6" => "N-6 (variable life separate account)",
            "S-1" or "S-6" => code + " (unit investment trust)",
            _ => code,
        };

    /// <summary>
    /// A holding balance without the noise ".00" share counts carry: whole quantities (the NS/NC
    /// cases) render with no decimals, fractional ones keep two.
    /// </summary>
    internal static string Balance(decimal balance) =>
        McpFormat.Invariant(balance, balance == decimal.Truncate(balance) ? "N0" : "N2");
}
