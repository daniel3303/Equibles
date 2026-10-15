namespace Equibles.Sec.FinancialFacts.Data.Statements;

/// <summary>
/// Recognises a fact unit that is an ISO 4217 currency. Filers also use three-letter units that are
/// not money (ADS for depositary shares, LBS, MWH, BTC), so a code must be on this list.
/// </summary>
public static class CurrencyUnits
{
    // Active ISO 4217 currencies plus the superseded codes filers still state (BYR, HRK, STD, VEB,
    // VEF, ZMK); fund and metal codes (CLF, XAU, XDR) are excluded.
    private static readonly HashSet<string> Codes = (
        "AED AFN ALL AMD ANG AOA ARS AUD AWG AZN BAM BBD BDT BGN BHD "
        + "BIF BMD BND BOB BRL BSD BTN BWP BYN BYR BZD CAD CDF CHF CLP "
        + "CNY COP CRC CUC CUP CVE CZK DJF DKK DOP DZD EGP ERN ETB EUR "
        + "FJD FKP GBP GEL GHS GIP GMD GNF GTQ GYD HKD HNL HRK HTG HUF "
        + "IDR ILS INR IQD IRR ISK JMD JOD JPY KES KGS KHR KMF KPW KRW "
        + "KWD KYD KZT LAK LBP LKR LRD LSL LYD MAD MDL MGA MKD MMK MNT "
        + "MOP MRU MUR MVR MWK MXN MYR MZN NAD NGN NIO NOK NPR NZD OMR "
        + "PAB PEN PGK PHP PKR PLN PYG QAR RON RSD RUB RWF SAR SBD SCR "
        + "SDG SEK SGD SHP SLE SLL SOS SRD SSP STD STN SVC SYP SZL THB "
        + "TJS TMT TND TOP TRY TTD TWD TZS UAH UGX USD UYU UZS VEB VED "
        + "VEF VES VND VUV WST XAF XCD XOF XPF YER ZAR ZMK ZMW ZWG ZWL "
    )
        .Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .ToHashSet(StringComparer.Ordinal);

    public static bool IsCurrency(string unit) => unit != null && Codes.Contains(unit);
}
