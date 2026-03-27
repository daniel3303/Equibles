using Equibles.Fred.Data.Models;

namespace Equibles.Fred.HostedService.Services;

public static class CuratedSeriesRegistry {
    public static readonly IReadOnlyList<CuratedSeries> Series = [
        // Interest Rates
        new("FEDFUNDS", FredSeriesCategory.InterestRates),
        new("EFFR", FredSeriesCategory.InterestRates),
        new("DFEDTARU", FredSeriesCategory.InterestRates),
        new("DFEDTARL", FredSeriesCategory.InterestRates),
        new("DPRIME", FredSeriesCategory.InterestRates),
        new("SOFR", FredSeriesCategory.InterestRates),

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
        new("STLFSI2", FredSeriesCategory.Market),
    ];
}

public record CuratedSeries(string SeriesId, FredSeriesCategory Category);
