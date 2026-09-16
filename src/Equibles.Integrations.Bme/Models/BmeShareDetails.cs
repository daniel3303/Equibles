namespace Equibles.Integrations.Bme.Models;

// The venue's own record of one share line: the ticker, the issuer code and the trading system it is admitted to.
public sealed class BmeShareDetails
{
    public string Name { get; set; }
    public string ShortName { get; set; }
    public string Isin { get; set; }
    public string Ticker { get; set; }
    public string IssuerCode { get; set; }
    public string Market { get; set; }
    public string TradingSystem { get; set; }
    public string Currency { get; set; }

    // The venue's own marker on the line itself, kept as evidence and never read as a listing state, unlike the
    // marker on an issuer's other lines. Whether a line is current comes from the venue's list and the FIRDS gate.
    public string Active { get; set; }
    public List<BmeIssuerShare> OtherSharesFromIssuer { get; set; } = [];
    public Uri SourceUrl { get; set; }
    public string Json { get; set; }
}

// Another line of the same issuer; a cancelled one carries its exclusion date and the active marker C.
public sealed class BmeIssuerShare
{
    public string Isin { get; set; }
    public string Name { get; set; }
    public string LongName { get; set; }
    public string Active { get; set; }
    public string ExclusionDate { get; set; }

    public bool IsCurrent => string.IsNullOrEmpty(Active) && string.IsNullOrEmpty(ExclusionDate);
}
