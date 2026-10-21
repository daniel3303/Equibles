using System.Globalization;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories.Models;
using Equibles.EquityMarkets.Data.Catalog;
using Equibles.Integrations.Yahoo.Models;
using Newtonsoft.Json;

namespace Equibles.Yahoo.HostedService.Services;

internal static class YahooListingSource
{
    // The cutoff is exclusive. Catalog venues finish their own session, not the UTC day;
    // the grace period lets the provider publish its closing auction before the first daily read.
    internal static DateOnly SettledBefore(PriceSeriesTarget target, DateTime utcNow)
    {
        if (target.IsUs || target.IsHistorical || Market(target) is not { } market)
            return DateOnly.FromDateTime(utcNow);
        var local = TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(utcNow, DateTimeKind.Utc),
            TimeZoneInfo.FindSystemTimeZoneById(market.TimeZoneId)
        );
        var day = DateOnly.FromDateTime(local);
        return local >= PublishAfter(market, day) ? day.AddDays(1) : day;
    }

    // The UTC instant the newest weekday session settled. An attempt before it has not yet asked the
    // provider for that session; one after it is a retry of the same session.
    internal static DateTime SettledSince(PriceSeriesTarget target, DateTime utcNow)
    {
        var cutoff = SettledBefore(target, utcNow);
        if (target.IsUs || target.IsHistorical || Market(target) is not { } market)
            return cutoff.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var session = cutoff.AddDays(-1);
        while (!HasWeekdaySession(session, session.AddDays(1)))
            session = session.AddDays(-1);
        return TimeZoneInfo.ConvertTimeToUtc(
            PublishAfter(market, session),
            TimeZoneInfo.FindSystemTimeZoneById(market.TimeZoneId)
        );
    }

    // Every catalog venue trades Monday to Friday, so a range holding only a weekend has no session
    // the provider could serve. Holidays are not modeled; only returned bars prove a trading date.
    internal static bool HasWeekdaySession(DateOnly start, DateOnly endExclusive)
    {
        for (var date = start; date < endExclusive; date = date.AddDays(1))
        {
            if (date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
                return true;
        }
        return false;
    }

    private static DateTime PublishAfter(EquityMarket market, DateOnly day)
    {
        var close =
            market.ClosingAuctionEnd > market.SessionClose
                ? market.ClosingAuctionEnd
                : market.SessionClose;
        return day.ToDateTime(close).AddMinutes(30);
    }

    // The catalog's suffix is only a candidate symbol: returned chart metadata must also pass MatchesChart,
    // and the quotation unit the chart reports must be the one the verified listing states.
    internal static EquityMarket Market(PriceSeriesTarget target)
    {
        if (
            target.IsUs
            || string.IsNullOrWhiteSpace(target.Isin)
                && (target.EquitySecurityId == null || target.EquitySecurityId == Guid.Empty)
        )
            return null;
        var market = EquityMarketCatalog.ByMarketIdentifierCode(target.MarketIdentifierCode);
        return
            market?.CountryCode == target.MarketCountryCode
            && !string.IsNullOrWhiteSpace(market.YahooSuffix)
            && !string.IsNullOrWhiteSpace(market.YahooExchangeCode)
            && Symbol(target.Ticker, market) != null
            ? market
            : null;
    }

    internal static string EvidenceSource(EquityMarket market) => $"yahoo-{market.Code}-chart-v1";

    internal static string ProviderSymbol(PriceSeriesTarget target) =>
        target.IsUs ? target.Ticker
        : Market(target) is { } market ? Symbol(target.Ticker, market)
        : null;

    // The provider symbol for a listing named by its venue alone; a market outside the catalog has no Yahoo identity.
    internal static string ProviderSymbol(
        string ticker,
        string marketCountryCode,
        string marketIdentifierCode
    )
    {
        if (string.IsNullOrWhiteSpace(ticker))
            return null;
        if (marketCountryCode == "US")
            return ticker;
        var market = EquityMarketCatalog.ByMarketIdentifierCode(marketIdentifierCode);
        return market?.CountryCode == marketCountryCode ? Symbol(ticker, market) : null;
    }

    private static string Symbol(string ticker, EquityMarket market)
    {
        if (string.IsNullOrWhiteSpace(ticker) || string.IsNullOrWhiteSpace(market.YahooSuffix))
            return null;
        if (market.NumericTickerWidth is { } sourceWidth && ticker.Length != sourceWidth)
            return null;
        if (market.YahooNumericSymbolWidth is not { } width)
            return ticker + market.YahooSuffix;
        // Exchange numeric codes and provider symbols can have different leading-zero widths.
        return
            ticker.All(char.IsAsciiDigit)
            && int.TryParse(ticker, NumberStyles.None, CultureInfo.InvariantCulture, out var code)
            && code > 0
            ? code.ToString(
                "D" + width.ToString(CultureInfo.InvariantCulture),
                CultureInfo.InvariantCulture
            ) + market.YahooSuffix
            : null;
    }

    internal static bool MatchesListing(PriceSeriesTarget target, EquityListing listing) =>
        listing.Id == target.EquityListingId
        && listing.Security.EquityIssuerId == target.EquityIssuerId
        && listing.Ticker == target.Ticker
        && listing.MarketCountryCode == target.MarketCountryCode
        && (
            target.IsHistorical
                ? !listing.Active && listing.DelistedOn == target.HistoryEndDate
                : listing.Active
        )
        && (
            target.IsUs
            || Market(target) != null
                && !target.IsHistorical
                && listing.IdentityState == EquityIdentityState.Verified
                && listing.MarketIdentifierCode == target.MarketIdentifierCode
                && listing.Security.Isin == target.Isin
                && (
                    target.EquitySecurityId == null
                    || listing.EquitySecurityId == target.EquitySecurityId
                )
                && listing.TradingCurrency != null
                && listing.TradingCurrency == target.TradingCurrency
                && listing.QuoteUnitMultiplier != null
                && listing.QuoteUnitMultiplier == target.QuoteUnitMultiplier
        );

    internal static bool MatchesChart(
        PriceSeriesTarget target,
        YahooChartSourceIdentity identity
    ) =>
        Market(target) is { } market
        && identity != null
        && identity.ExchangeCode == market.YahooExchangeCode
        && identity.InstrumentType == "EQUITY"
        && identity.ExchangeTimeZone == market.TimeZoneId
        && EquityQuotationUnits.Matches(
            identity.Currency,
            target.TradingCurrency,
            target.QuoteUnitMultiplier
        )
        && string.Equals(identity.Symbol, ProviderSymbol(target), StringComparison.Ordinal);

    internal static EquityListingSourceBinding SourceBinding(PriceSeriesTarget target)
    {
        var market =
            Market(target)
            ?? throw new InvalidOperationException("Only catalog markets have a Yahoo binding.");
        return new(
            target.EquityIssuerId,
            target.EquityListingId,
            target.Ticker,
            target.MarketIdentifierCode,
            target.MarketCountryCode,
            target.Isin,
            target.TradingCurrency,
            target.QuoteUnitMultiplier
                ?? throw new InvalidOperationException(
                    "A catalog binding needs the listing's verified quotation unit."
                ),
            market.MarketIdentifierCodes.ToArray(),
            target.EquitySecurityId
        );
    }

    internal static EquityListingQuotationEvidence QuotationEvidence(
        PriceSeriesTarget target,
        YahooChartSourceIdentity identity
    )
    {
        var market = Market(target);
        return new(
            SourceBinding(target),
            EvidenceSource(market),
            JsonConvert.SerializeObject(
                new
                {
                    target.EquityIssuerId,
                    target.EquityListingId,
                    target.EquitySecurityId,
                    target.Ticker,
                    target.MarketIdentifierCode,
                    target.Isin,
                    target.TradingCurrency,
                    target.QuoteUnitMultiplier,
                    Market = market.Code,
                    RequestedSymbol = ProviderSymbol(target),
                    SourceMarkets = market.MarketIdentifierCodes,
                    SourceUrl = "https://query1.finance.yahoo.com/v8/finance/chart/"
                        + Uri.EscapeDataString(ProviderSymbol(target)),
                    SourceIdentity = identity,
                }
            )
        );
    }
}
