using Equibles.Mcp.Helpers;

namespace Equibles.Sec.Mcp.Tools;

/// <summary>
/// Render-time glosses for the raw SEC enumeration codes the fund tools would otherwise leak
/// verbatim ("NS", "EC", "S-6"). Each gloss keeps the original code visible and appends a short
/// human label, so an LLM consumer needs no out-of-band legend while a consumer keying on the raw
/// code still finds it. Unknown codes pass through unchanged — the glosses annotate, never gate.
/// </summary>
internal static class FundCodes
{
    /// <summary>NPORT-P holding unit codes (Item C.7): what <c>Balance</c> is denominated in.</summary>
    internal static string Unit(string code) =>
        code switch
        {
            null or "" => "-",
            "NS" => "NS (shares)",
            "PA" => "PA (principal amount)",
            "NC" => "NC (contracts)",
            _ => code,
        };

    /// <summary>NPORT-P asset-category codes (Item C.4.a).</summary>
    internal static string AssetCategory(string code) =>
        code switch
        {
            null or "" => "-",
            "EC" => "EC (equity-common)",
            "EP" => "EP (equity-preferred)",
            "DBT" => "DBT (debt)",
            "STIV" => "STIV (short-term investment vehicle)",
            "RA" => "RA (repurchase agreement)",
            "LON" => "LON (loan)",
            "SN" => "SN (structured note)",
            "RE" => "RE (real estate)",
            "COMM" => "COMM (commodity)",
            "DE" => "DE (derivative-equity)",
            "DCO" => "DCO (derivative-commodity)",
            "DCR" => "DCR (derivative-credit)",
            "DFE" => "DFE (derivative-foreign exchange)",
            "DIR" => "DIR (derivative-interest rate)",
            "DO" => "DO (derivative-other)",
            "ABS-MBS" => "ABS-MBS (mortgage-backed security)",
            "ABS-APCP" => "ABS-APCP (asset-backed commercial paper)",
            "ABS-CBDO" => "ABS-CBDO (collateralized bond/debt obligation)",
            "ABS-O" => "ABS-O (other asset-backed security)",
            _ => code,
        };

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
