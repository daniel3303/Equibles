using System.Text.Json;
using Equibles.CommonStocks.BusinessLogic.Directory;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.AutoWiring;
using Equibles.Core.Identity;
using Equibles.EquityMarkets.Data.Catalog;
using Equibles.EquityMarkets.Data.Models;
using Equibles.EquityMarkets.Repositories;
using Equibles.Integrations.Gleif;
using Equibles.Integrations.Gleif.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Equibles.EquityMarkets.BusinessLogic.Directory;

// One pass over a market: the venue's directory is reconciled whole, then each row the gate confirms as the
// market's own share listing is verified against the source product and GLEIF before it becomes a listing. A venue's
// exchange-traded products take a second pass of their own, admitted on every venue that lists them.
[Service]
public class EquityMarketDirectoryImporter(
    IEnumerable<IEquityMarketDirectorySource> sources,
    IEnumerable<IExchangeTradedProductDirectorySource> productSources,
    GleifIdentityClient gleifClient,
    EquityDirectoryIdentityImporter identityImporter,
    EquityDirectorySnapshotManager snapshotManager,
    IServiceScopeFactory scopeFactory,
    ILogger<EquityMarketDirectoryImporter> logger
)
{
    internal static readonly TimeSpan ReverificationInterval = TimeSpan.FromDays(30);

    public async Task<EquityMarketDirectoryImportResult> Import(
        EquityMarket market,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(market);
        var source =
            sources.FirstOrDefault(candidate =>
                candidate.SourceKey == market.DirectorySource && candidate.Supports(market)
            )
            ?? throw new InvalidOperationException(
                $"No directory source serves {market.Code} ({market.DirectorySource ?? "none"})."
            );
        return await ImportRows(
            market,
            source.SourceKey,
            products: false,
            token => source.Capture(market, token),
            (row, firds, token) => source.Resolve(market, row, firds, token),
            cancellationToken
        );
    }

    // Whether the market's venue publishes a list of exchange-traded products a pass reads.
    public bool HasProductSource(EquityMarket market) =>
        market != null && productSources.Any(candidate => candidate.Supports(market));

    public async Task<EquityMarketDirectoryImportResult> ImportExchangeTradedProducts(
        EquityMarket market,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(market);
        var source =
            productSources.FirstOrDefault(candidate => candidate.Supports(market))
            ?? throw new InvalidOperationException(
                $"No exchange-traded product source serves {market.Code}."
            );
        return await ImportRows(
            market,
            source.SourceKey,
            products: true,
            token => source.CaptureProducts(market, token),
            (row, firds, token) => source.Resolve(market, row, firds, token),
            cancellationToken
        );
    }

    private async Task<EquityMarketDirectoryImportResult> ImportRows(
        EquityMarket market,
        string sourceKey,
        bool products,
        Func<CancellationToken, Task<EquityMarketDirectorySnapshot>> capture,
        Func<
            EquityMarketDirectoryRow,
            FirdsInstrumentRecord,
            CancellationToken,
            Task<EquityMarketDirectoryProduct>
        > resolve,
        CancellationToken cancellationToken
    )
    {
        var result = new EquityMarketDirectoryImportResult();
        using (var scope = scopeFactory.CreateScope())
        {
            var runs = scope.ServiceProvider.GetRequiredService<FirdsImportRunRepository>();
            // Funds come from FIRDS's collective-investment files, commodities and notes from its equity files.
            if (
                !await runs.HasFullImport(
                    market.FirdsAuthority,
                    cancellationToken: cancellationToken
                )
                || products
                    && !await runs.HasFullImport(market.FirdsAuthority, 'C', cancellationToken)
            )
            {
                result.Error = "FIRDS universe is not loaded yet; the directory pass waits for it.";
                return result;
            }
        }
        var snapshot = await capture(cancellationToken);
        result.Listings = snapshot.Rows.Count;
        result.Excluded = snapshot.Excluded;
        var snapshotId = await snapshotManager.Reconcile(
            new EquityDirectorySnapshotInput
            {
                Source = sourceKey,
                EvidenceSource = snapshot.EvidenceSource,
                SourceRecordKey = snapshot.SourceUrl.AbsoluteUri,
                PayloadJson = snapshot.PayloadJson,
                ObservedAt = snapshot.CapturedAt,
                MarketCountryCode = market.CountryCode,
                MarketIdentifierCodes = market.MarketIdentifierCodes,
                ExchangeTradedProducts = products,
                Listings = snapshot
                    .Rows.Select(row => new EquityDirectoryListingKey(
                        row.Isin,
                        row.MarketIdentifierCode,
                        row.Symbol
                    ))
                    .ToList(),
            },
            cancellationToken
        );
        foreach (var row in snapshot.Rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var now = DateTime.UtcNow;
            FirdsInstrumentRecord firds;
            List<FirdsInstrumentRecord> lines = null;
            using (var scope = scopeFactory.CreateScope())
            {
                var records =
                    scope.ServiceProvider.GetRequiredService<FirdsInstrumentRecordRepository>();
                if (products)
                {
                    firds = await records.GetLiveProduct(
                        row.Isin,
                        market.FirdsAuthority,
                        market.FirdsVenueCodes,
                        now,
                        cancellationToken
                    );
                    if (!EquityMarketDirectoryGate.IsExchangeTradedProductLine(market, row, firds))
                    {
                        result.Skipped++;
                        continue;
                    }
                }
                else
                {
                    firds = await records.GetLiveShare(
                        row.Isin,
                        market.FirdsAuthority,
                        market.FirdsVenueCodes,
                        now,
                        cancellationToken
                    );
                    // The share's other lines matter only once the relevant venue alone refuses it.
                    if (firds != null && !EquityMarketDirectoryGate.IsHomeShare(market, row, firds))
                        lines = await records.GetLiveShareLines(row.Isin, now, cancellationToken);
                    if (!EquityMarketDirectoryGate.IsHomeShare(market, row, firds, lines))
                    {
                        result.Skipped++;
                        continue;
                    }
                }
                if (
                    await IsCurrent(
                        scope.ServiceProvider,
                        market,
                        sourceKey,
                        row,
                        firds.Lei,
                        now,
                        cancellationToken
                    )
                )
                {
                    result.Current++;
                    continue;
                }
            }
            // Bounds a wedged row; the source and GLEIF clients carry their own two-minute budgets, and a
            // throttled GLEIF lookup waits out the shared pace and its retries before it resolves.
            using var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            attempt.CancelAfter(TimeSpan.FromMinutes(5));
            try
            {
                var product = await resolve(row, firds, attempt.Token);
                var issuer = await gleifClient.GetIssuerForIsin(row.Isin, attempt.Token);
                var firdsIssuer =
                    issuer.LegalEntityIdentifier == null
                    && InternationalSecurityIdentifiers.IsValidLei(firds.Lei)
                        ? await gleifClient.GetIssuerForLei(firds.Lei, attempt.Token)
                        : null;
                // A UCITS sub-fund: GLEIF maps its ISIN to the umbrella company while FIRDS names the fund itself.
                string umbrella = null;
                GleifIssuerIdentity subFund = null;
                if (
                    products
                    && firdsIssuer == null
                    && issuer.LegalEntityIdentifier != null
                    && InternationalSecurityIdentifiers.IsValidLei(firds.Lei)
                    && issuer.LegalEntityIdentifier != firds.Lei
                )
                {
                    umbrella = await gleifClient.GetUmbrellaFundLei(firds.Lei, attempt.Token);
                    if (umbrella == issuer.LegalEntityIdentifier)
                        subFund = await gleifClient.GetIssuerForLei(firds.Lei, attempt.Token);
                }
                var input = CreateInput(
                    market,
                    sourceKey,
                    row,
                    product,
                    firds,
                    issuer,
                    firdsIssuer,
                    lines,
                    products,
                    subFund,
                    umbrella
                );
                input.DirectorySnapshotId = snapshotId;
                await identityImporter.ImportListing(input, attempt.Token);
                result.Imported++;
            }
            catch (OperationCanceledException exception)
                when (!cancellationToken.IsCancellationRequested)
            {
                result.Failed++;
                logger.LogWarning(
                    exception,
                    "{Market} identity lookup timed out for {Isin} on {Mic}; continuing with later records",
                    market.Code,
                    row.Isin,
                    row.MarketIdentifierCode
                );
            }
            catch (Exception exception)
                when (exception is InvalidDataException or HttpRequestException or JsonException)
            {
                result.Failed++;
                logger.LogWarning(
                    exception,
                    "{Market} directory identity remains unresolved for {Isin} on {Mic}",
                    market.Code,
                    row.Isin,
                    row.MarketIdentifierCode
                );
            }
        }
        logger.LogInformation(
            "{Market} {Kind} directory cycle complete: {Imported} imported, {Current} current, {Skipped} refused by the gate, {Failed} unresolved, {Excluded} lines the source could not carry, {Total} source listings",
            market.Code,
            products ? "product" : "share",
            result.Imported,
            result.Current,
            result.Skipped,
            result.Failed,
            result.Excluded,
            result.Listings
        );
        return result;
    }

    // A verified listing whose directory row is unchanged is re-verified monthly, not daily: GLEIF and the
    // product page are the expensive calls, and a changed symbol, ISIN, venue or URL always re-verifies.
    private static async Task<bool> IsCurrent(
        IServiceProvider services,
        EquityMarket market,
        string sourceKey,
        EquityMarketDirectoryRow row,
        string firdsLei,
        DateTime now,
        CancellationToken cancellationToken
    )
    {
        var listings = services.GetRequiredService<EquityListingRepository>();
        var url = row.SourceUrl.AbsoluteUri;
        var directoryName = row.Name?.Trim();
        var canResolveLei = InternationalSecurityIdentifiers.IsValidLei(firdsLei);
        var verified = await listings
            .GetAll()
            .Where(listing =>
                listing.Active
                && listing.IdentityState == EquityIdentityState.Verified
                && listing.MarketIdentifierCode == row.MarketIdentifierCode
                && listing.Ticker == row.Symbol
                && listing.Security.Isin == row.Isin
                && (!canResolveLei || listing.Security.Issuer.LegalEntityIdentifier != null)
                && listing.IdentitySourceUrl == url
            )
            .Select(listing => new { listing.Security.Issuer.NameAliases })
            .FirstOrDefaultAsync(cancellationToken);
        if (
            verified == null
            || directoryName != null
                && !verified.NameAliases.Contains(directoryName, StringComparer.OrdinalIgnoreCase)
        )
            return false;
        var evidence = services.GetRequiredService<EquityDirectorySourceRecordRepository>();
        var lastCapture = await evidence
            .GetAll()
            .Where(record => record.Source == sourceKey && record.SourceRecordKey == url)
            .MaxAsync(record => (DateTime?)record.CapturedAt, cancellationToken);
        if (lastCapture == null || lastCapture <= now - ReverificationInterval)
            return false;
        // A regular line re-imports while its share is still presented on an extended-session line.
        if (
            market.ExtendedSessionCodes.Count == 0
            || market.IsExtendedSession(row.MarketIdentifierCode)
        )
            return true;
        var sessionCodes = market.ExtendedSessionCodes.ToArray();
        return !await listings
            .GetAll()
            .AnyAsync(
                listing =>
                    listing.Presentation != null
                    && listing.Security.Isin == row.Isin
                    && sessionCodes.Contains(listing.MarketIdentifierCode),
                cancellationToken
            );
    }

    internal static EquityDirectoryListingInput CreateInput(
        EquityMarket market,
        string sourceKey,
        EquityMarketDirectoryRow row,
        EquityMarketDirectoryProduct product,
        FirdsInstrumentRecord firds,
        GleifIssuerIdentity issuer,
        GleifIssuerIdentity firdsIssuer = null,
        IReadOnlyCollection<FirdsInstrumentRecord> firdsLines = null,
        bool exchangeTradedProduct = false,
        GleifIssuerIdentity subFund = null,
        string subFundUmbrella = null
    )
    {
        ArgumentNullException.ThrowIfNull(market);
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(product);
        ArgumentNullException.ThrowIfNull(firds);
        ArgumentNullException.ThrowIfNull(issuer);
        if (
            firdsIssuer != null
            && (
                issuer.LegalEntityIdentifier != null
                || issuer.RelatedIsins.Count != 0
                || !InternationalSecurityIdentifiers.IsValidLei(firds.Lei)
                || firdsIssuer.RequestedLei != firds.Lei
                || firdsIssuer.LegalEntityIdentifier != firds.Lei
                || firdsIssuer.RequestedIsin != null
                || firdsIssuer.RelatedIsins.Count != 0
            )
        )
            throw new InvalidDataException(
                "FIRDS issuer recovery requires an exact independently confirmed LEI."
            );
        // The fund FIRDS names is the issuer only when GLEIF places it under the umbrella its ISIN maps to.
        if (
            subFund != null
            && (
                !exchangeTradedProduct
                || firdsIssuer != null
                || !InternationalSecurityIdentifiers.IsValidLei(firds.Lei)
                || subFund.RequestedLei != firds.Lei
                || subFund.LegalEntityIdentifier != firds.Lei
                || issuer.LegalEntityIdentifier == null
                || issuer.LegalEntityIdentifier == firds.Lei
                || subFundUmbrella != issuer.LegalEntityIdentifier
            )
        )
            throw new InvalidDataException(
                "A sub-fund issuer requires the fund FIRDS names under the umbrella GLEIF maps the ISIN to."
            );
        var confirmedIssuer = subFund ?? firdsIssuer ?? issuer;
        if (
            row.SourceUrl == null
            || product.SourceUrl != row.SourceUrl
            || issuer.RequestedIsin != row.Isin
            || (
                exchangeTradedProduct
                    ? !EquityMarketDirectoryGate.IsExchangeTradedProductLine(market, row, firds)
                    : !EquityMarketDirectoryGate.IsHomeShare(market, row, firds, firdsLines)
            )
        )
            throw new InvalidDataException(
                "Directory identity sources disagree on the requested security."
            );
        if (
            confirmedIssuer.LegalEntityIdentifier != null
            && (
                confirmedIssuer.EntityStatus != "ACTIVE"
                || confirmedIssuer.RegistrationStatus is not ("ISSUED" or "LAPSED")
            )
        )
            throw new InvalidDataException("GLEIF issuer is not a current legal identity.");
        if (
            confirmedIssuer.LegalEntityIdentifier != null
            && firds.Lei != null
            && confirmedIssuer.LegalEntityIdentifier != firds.Lei
        )
            throw new InvalidDataException("FIRDS and GLEIF disagree on the security's issuer.");
        var token = product.ReportedCurrency ?? row.ReportedCurrency;
        var quoted = EquityQuotationUnits.TryResolve(token, out var currency, out var multiplier);
        return new EquityDirectoryListingInput
        {
            Source = sourceKey,
            SourceIssuerIdentifier = product.SourceIssuerIdentifier,
            IssuerName = confirmedIssuer.LegalName ?? product.Name ?? row.Name,
            IssuerNameAliases = new[] { row.Name, product.Name }
                .Concat(product.NameAliases)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToList(),
            LegalEntityIdentifier = confirmedIssuer.LegalEntityIdentifier,
            // A fund issuer's GLEIF ISINs are its sibling funds (an umbrella lists every sub-fund), never this
            // product's own securities, so a product line claims only its ISIN.
            RelatedIsins =
                firdsIssuer == null && subFund == null && !exchangeTradedProduct
                    ? issuer.RelatedIsins
                    : [row.Isin],
            Isin = row.Isin,
            Ticker = row.Symbol,
            MarketIdentifierCode = row.MarketIdentifierCode,
            MarketCountryCode = market.CountryCode,
            ExtendedSessionMarketIdentifierCodes = [.. market.ExtendedSessionCodes],
            TradingCurrency = quoted ? currency : null,
            QuoteUnitMultiplier = quoted ? multiplier : null,
            SecurityType = exchangeTradedProduct
                ? EquityMarketDirectoryGate.ProductKind(firds.Cfi)
                : null,
            SourceUrl = product.SourceUrl.AbsoluteUri,
            PayloadJson = JsonSerializer.Serialize(
                new
                {
                    Market = market.Code,
                    Row = row,
                    Product = product.Evidence,
                    Firds = new
                    {
                        firds.Authority,
                        firds.Isin,
                        firds.Mic,
                        firds.Lei,
                        firds.Cfi,
                        firds.Currency,
                        firds.FullName,
                        firds.RelevantCompetentAuthority,
                        firds.RelevantTradingVenue,
                        firds.IssuerRequested,
                        firds.FirstTradeDate,
                        firds.TerminationDate,
                    },
                    Issuer = issuer,
                    FirdsIssuer = firdsIssuer,
                    SubFund = subFund,
                    SubFundUmbrella = subFundUmbrella,
                }
            ),
        };
    }
}
