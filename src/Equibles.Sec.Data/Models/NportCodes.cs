namespace Equibles.Sec.Data.Models;

/// <summary>
/// Glosses for the short codes NPORT-P states on a holding ("NS", "EC"). Each keeps the filed code
/// visible and appends a short label, so a reader needs no legend while a consumer keying on the
/// code still finds it. Unknown codes pass through unchanged: the glosses annotate, never gate.
/// </summary>
public static class NportCodes
{
    /// <summary>NPORT-P holding unit codes (Item C.7): what <c>Balance</c> is denominated in.</summary>
    public static string Unit(string code) =>
        code switch
        {
            null or "" => "-",
            "NS" => "NS (shares)",
            "PA" => "PA (principal amount)",
            "NC" => "NC (contracts)",
            _ => code,
        };

    /// <summary>NPORT-P asset-category codes (Item C.4.a).</summary>
    public static string AssetCategory(string code) =>
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
}
