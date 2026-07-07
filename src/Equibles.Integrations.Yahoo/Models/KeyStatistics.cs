namespace Equibles.Integrations.Yahoo.Models;

public class KeyStatistics
{
    public long SharesOutstanding { get; set; }

    /// <summary>
    /// The entity-wide share count (all classes, converted into the quoted listing's units) that
    /// Yahoo builds <see cref="MarketCapitalization"/> on, or 0 when Yahoo omits it. For a
    /// multi-class issuer this exceeds <see cref="SharesOutstanding"/>, which covers only the
    /// quoted class.
    /// </summary>
    public long ImpliedSharesOutstanding { get; set; }

    public double MarketCapitalization { get; set; }
}
