using Equibles.Fred.Data.Models;

namespace Equibles.Fred.HostedService.Services;

// The FRED series this deployment tracks. Every entry becomes a stored series with its full
// observation history, one public page, and one sitemap URL.
//
// Curated rather than swept: FRED publishes around 800,000 series and almost all of them are a
// county-level or vintage-specific slice nobody asks for. The list below is the set a reader
// (or a model answering about the US economy) actually names, chosen against FRED's own
// popularity score with a floor of 50, and it deliberately fills out the families already here
// rather than opening new ones. Two series that cleared the floor are excluded on purpose:
// USSLIND stopped updating in April 2020, and FPCPITOTLZGUSA is an annual World Bank restatement
// of CPI that lags the series we already carry. A frozen series is worse than a missing one; it
// renders a current-looking page over a stale number, which is the same trap STLFSI2 sprang.
public static class CuratedSeriesRegistry
{
    public static readonly IReadOnlyList<CuratedSeries> Series =
    [
        // Interest Rates
        new("FEDFUNDS", FredSeriesCategory.InterestRates),
        new("EFFR", FredSeriesCategory.InterestRates),
        new("DFEDTARU", FredSeriesCategory.InterestRates),
        new("DFEDTARL", FredSeriesCategory.InterestRates),
        new("DPRIME", FredSeriesCategory.InterestRates),
        new("SOFR", FredSeriesCategory.InterestRates),
        // Treasury constant-maturity yields — the most-watched rates on FRED, and the
        // inputs behind the T10Y2Y/T10Y3M spreads already curated below.
        new("DGS2", FredSeriesCategory.InterestRates),
        new("DGS10", FredSeriesCategory.InterestRates),
        new("DGS30", FredSeriesCategory.InterestRates),
        // Yield Spreads
        new("T10Y2Y", FredSeriesCategory.YieldSpreads),
        new("T10Y3M", FredSeriesCategory.YieldSpreads),
        // Corporate Bond Spreads
        new("BAMLH0A0HYM2", FredSeriesCategory.CorporateBondSpreads),
        new("BAMLC0A0CM", FredSeriesCategory.CorporateBondSpreads),
        new("AAA", FredSeriesCategory.CorporateBondSpreads),
        new("BAA", FredSeriesCategory.CorporateBondSpreads),
        // Inflation
        new("CPIAUCSL", FredSeriesCategory.Inflation),
        new("CPILFESL", FredSeriesCategory.Inflation),
        new("PPIFIS", FredSeriesCategory.Inflation),
        new("PPIFES", FredSeriesCategory.Inflation),
        new("PCEPILFE", FredSeriesCategory.Inflation),
        new("T10YIE", FredSeriesCategory.Inflation),
        new("T5YIFR", FredSeriesCategory.Inflation),
        // Employment
        new("UNRATE", FredSeriesCategory.Employment),
        new("PAYEMS", FredSeriesCategory.Employment),
        new("ICSA", FredSeriesCategory.Employment),
        new("JTSJOL", FredSeriesCategory.Employment),
        // GDP & Output
        new("GDP", FredSeriesCategory.GdpAndOutput),
        new("GDPC1", FredSeriesCategory.GdpAndOutput),
        new("INDPRO", FredSeriesCategory.GdpAndOutput),
        new("RSAFS", FredSeriesCategory.GdpAndOutput),
        // Money Supply
        new("M2SL", FredSeriesCategory.MoneySupply),
        new("WALCL", FredSeriesCategory.MoneySupply),
        // Sentiment
        new("UMCSENT", FredSeriesCategory.Sentiment),
        // Housing
        new("HOUST", FredSeriesCategory.Housing),
        new("CSUSHPINSA", FredSeriesCategory.Housing),
        new("MORTGAGE30US", FredSeriesCategory.Housing),
        // Exchange Rates
        new("DTWEXBGS", FredSeriesCategory.ExchangeRates),
        new("DEXUSEU", FredSeriesCategory.ExchangeRates),
        // Market
        new("SP500", FredSeriesCategory.Market),
        new("VIXCLS", FredSeriesCategory.Market),
        new("NFCI", FredSeriesCategory.Market),
        // STLFSI4 supersedes the discontinued STLFSI2 (frozen at 2022-01-07), whose
        // stale value polluted the "current macro conditions" snapshot.
        new("STLFSI4", FredSeriesCategory.Market),
        // The rest of the Treasury constant-maturity curve plus the Fed's own administered
        // rates. A curve with only the 2s, 10s and 30s cannot answer a question about the front end.
        new("DGS5", FredSeriesCategory.InterestRates),
        new("DFF", FredSeriesCategory.InterestRates),
        new("DGS1", FredSeriesCategory.InterestRates),
        new("IORB", FredSeriesCategory.InterestRates),
        new("DGS3MO", FredSeriesCategory.InterestRates),
        new("DGS20", FredSeriesCategory.InterestRates),
        new("DGS1MO", FredSeriesCategory.InterestRates),
        new("TB3MS", FredSeriesCategory.InterestRates),
        new("DGS3", FredSeriesCategory.InterestRates),
        new("DGS6MO", FredSeriesCategory.InterestRates),
        new("DGS7", FredSeriesCategory.InterestRates),
        // The rating buckets under the aggregate high-yield and investment-grade spreads: BB, B
        // and CCC separate a risk-on rally from a genuine credit event, which BAMLH0A0HYM2 alone cannot.
        new("BAMLC0A4CBBB", FredSeriesCategory.CorporateBondSpreads),
        new("BAMLH0A3HYC", FredSeriesCategory.CorporateBondSpreads),
        new("DBAA", FredSeriesCategory.CorporateBondSpreads),
        new("BAMLH0A1HYBB", FredSeriesCategory.CorporateBondSpreads),
        new("DAAA", FredSeriesCategory.CorporateBondSpreads),
        new("BAMLH0A2HYB", FredSeriesCategory.CorporateBondSpreads),
        new("BAMLC0A1CAAAEY", FredSeriesCategory.CorporateBondSpreads),
        // The unadjusted CPI everyone quotes, the PCE price level, the market-implied and survey
        // expectation measures, and the three component indices (food, shelter, energy) that
        // explain a headline print the aggregate only reports.
        new("T5YIE", FredSeriesCategory.Inflation),
        new("PCEPI", FredSeriesCategory.Inflation),
        new("CPIAUCNS", FredSeriesCategory.Inflation),
        new("MICH", FredSeriesCategory.Inflation),
        new("CPIUFDSL", FredSeriesCategory.Inflation),
        new("CUSR0000SEHA", FredSeriesCategory.Inflation),
        new("CPIENGSL", FredSeriesCategory.Inflation),
        // The rest of the household survey. The headline unemployment rate is the one number whose
        // meaning changes once participation, U-6, hourly earnings and the demographic breakdowns
        // are next to it, and the JOLTS hires and quits behind the openings series already here.
        new("CIVPART", FredSeriesCategory.Employment),
        new("CES0500000003", FredSeriesCategory.Employment),
        new("U6RATE", FredSeriesCategory.Employment),
        new("CCSA", FredSeriesCategory.Employment),
        new("EMRATIO", FredSeriesCategory.Employment),
        new("UNEMPLOY", FredSeriesCategory.Employment),
        new("LNS14000006", FredSeriesCategory.Employment),
        new("AWHAETP", FredSeriesCategory.Employment),
        new("JTSHIL", FredSeriesCategory.Employment),
        // The demand and capacity side of the same accounts GDP already covers.
        new("PCE", FredSeriesCategory.GdpAndOutput),
        new("A191RL1Q225SBEA", FredSeriesCategory.GdpAndOutput),
        new("GDPPOT", FredSeriesCategory.GdpAndOutput),
        new("TCU", FredSeriesCategory.GdpAndOutput),
        new("DGORDER", FredSeriesCategory.GdpAndOutput),
        new("GPDI", FredSeriesCategory.GdpAndOutput),
        new("BUSINV", FredSeriesCategory.GdpAndOutput),
        new("NETEXP", FredSeriesCategory.GdpAndOutput),
        // Reserve-side plumbing. The overnight reverse repo facility is the most-watched of these
        // and was missing entirely.
        new("RRPONTSYD", FredSeriesCategory.MoneySupply),
        new("WRESBAL", FredSeriesCategory.MoneySupply),
        new("M2V", FredSeriesCategory.MoneySupply),
        new("M1SL", FredSeriesCategory.MoneySupply),
        new("BOGMBASE", FredSeriesCategory.MoneySupply),
        new("TOTRESNS", FredSeriesCategory.MoneySupply),
        // Prices, supply and the 15-year mortgage. Housing had starts and the 30-year rate and
        // nothing that says what a house costs.
        new("MSPUS", FredSeriesCategory.Housing),
        new("MORTGAGE15US", FredSeriesCategory.Housing),
        new("EXHOSLUSM495S", FredSeriesCategory.Housing),
        new("HSN1F", FredSeriesCategory.Housing),
        new("RHORUSQ156N", FredSeriesCategory.Housing),
        new("PERMIT", FredSeriesCategory.Housing),
        new("MSACSR", FredSeriesCategory.Housing),
        // The major bilateral pairs behind the trade-weighted dollar index we already carry.
        new("DEXJPUS", FredSeriesCategory.ExchangeRates),
        new("DEXCHUS", FredSeriesCategory.ExchangeRates),
        new("DEXUSUK", FredSeriesCategory.ExchangeRates),
        new("DEXCAUS", FredSeriesCategory.ExchangeRates),
        new("DEXKOUS", FredSeriesCategory.ExchangeRates),
        new("DEXINUS", FredSeriesCategory.ExchangeRates),
        new("DEXMXUS", FredSeriesCategory.ExchangeRates),
        new("DTWEXAFEGS", FredSeriesCategory.ExchangeRates),
        new("DEXSZUS", FredSeriesCategory.ExchangeRates),
        new("DEXBZUS", FredSeriesCategory.ExchangeRates),
        // The other two headline indices, both crude benchmarks, retail gasoline, and the
        // adjusted financial-conditions index that pairs with NFCI.
        new("DCOILWTICO", FredSeriesCategory.Market),
        new("DCOILBRENTEU", FredSeriesCategory.Market),
        new("GASREGW", FredSeriesCategory.Market),
        new("NASDAQCOM", FredSeriesCategory.Market),
        new("DJIA", FredSeriesCategory.Market),
        new("NASDAQ100", FredSeriesCategory.Market),
        new("ANFCI", FredSeriesCategory.Market),
    ];
}

public record CuratedSeries(string SeriesId, FredSeriesCategory Category);
