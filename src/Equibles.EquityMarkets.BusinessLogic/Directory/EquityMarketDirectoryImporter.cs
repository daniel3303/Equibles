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
// market's own share listing is verified against the source product and GLEIF before it becomes a listing.
[Service]
public class EquityMarketDirectoryImporter(
    IEnumerable<IEquityMarketDirectorySource> sources,
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
        var result = new EquityMarketDirectoryImportResult();
        using (var scope = scopeFactory.CreateScope())
        {
            var runs = scope.ServiceProvider.GetRequiredService<FirdsImportRunRepository>();
            if (!await runs.HasFullImport(market.FirdsAuthority, cancellationToken))
            {
                result.Error = "FIRDS universe is not loaded yet; the directory pass waits for it.";
                return result;
            }
        }
        var snapshot = await source.Capture(market, cancellationToken);
        result.Listings = snapshot.Rows.Count;
        result.Excluded = snapshot.Excluded;
        var snapshotId = await snapshotManager.Reconcile(
            new EquityDirectorySnapshotInput
            {
                Source = source.SourceKey,
                EvidenceSource = snapshot.EvidenceSource,
                SourceRecordKey = snapshot.SourceUrl.AbsoluteUri,
                PayloadJson = snapshot.PayloadJson,
                ObservedAt = snapshot.CapturedAt,
                MarketCountryCode = market.CountryCode,
                MarketIdentifierCodes = market.MarketIdentifierCodes,
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
            using (var scope = scopeFactory.CreateScope())
            {
                var records =
                    scope.ServiceProvider.GetRequiredService<FirdsInstrumentRecordRepository>();
                firds = await records.GetLiveShare(
                    row.Isin,
                    market.FirdsAuthority,
                    market.FirdsVenueCodes,
                    now,
                    cancellationToken
                );
                if (!EquityMarketDirectoryGate.IsHomeShare(market, row, firds))
                {
                    result.Skipped++;
                    continue;
                }
                if (
                    await IsCurrent(
                        scope.ServiceProvider,
                        source.SourceKey,
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
                var product = await source.Resolve(market, row, firds, attempt.Token);
                var issuer = await gleifClient.GetIssuerForIsin(row.Isin, attempt.Token);
                var firdsIssuer =
                    issuer.LegalEntityIdentifier == null
                    && InternationalSecurityIdentifiers.IsValidLei(firds.Lei)
                        ? await gleifClient.GetIssuerForLei(firds.Lei, attempt.Token)
                        : null;
                var input = CreateInput(
                    market,
                    source.SourceKey,
                    row,
                    product,
                    firds,
                    issuer,
                    firdsIssuer
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
            "{Market} directory cycle complete: {Imported} imported, {Current} current, {Skipped} not the market's own share listings, {Failed} unresolved, {Excluded} share lines the source could not carry, {Total} source listings",
            market.Code,
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
        string sourceKey,
        EquityMarketDirectoryRow row,
        string firdsLei,
        DateTime now,
        CancellationToken cancellationToken
    )
    {
        var listings = services.GetRequiredService<EquityListingRepository>();
        var url = row.SourceUrl.AbsoluteUri;
        var canResolveLei = InternationalSecurityIdentifiers.IsValidLei(firdsLei);
        var verified = await listings
            .GetAll()
            .AnyAsync(
                listing =>
                    listing.Active
                    && listing.IdentityState == EquityIdentityState.Verified
                    && listing.MarketIdentifierCode == row.MarketIdentifierCode
                    && listing.Ticker == row.Symbol
                    && listing.Security.Isin == row.Isin
                    && (!canResolveLei || listing.Security.Issuer.LegalEntityIdentifier != null)
                    && listing.IdentitySourceUrl == url,
                cancellationToken
            );
        if (!verified)
            return false;
        var evidence = services.GetRequiredService<EquityDirectorySourceRecordRepository>();
        var lastCapture = await evidence
            .GetAll()
            .Where(record => record.Source == sourceKey && record.SourceRecordKey == url)
            .MaxAsync(record => (DateTime?)record.CapturedAt, cancellationToken);
        return lastCapture != null && lastCapture > now - ReverificationInterval;
    }

    internal static EquityDirectoryListingInput CreateInput(
        EquityMarket market,
        string sourceKey,
        EquityMarketDirectoryRow row,
        EquityMarketDirectoryProduct product,
        FirdsInstrumentRecord firds,
        GleifIssuerIdentity issuer,
        GleifIssuerIdentity firdsIssuer = null
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
        var confirmedIssuer = firdsIssuer ?? issuer;
        if (
            row.SourceUrl == null
            || product.SourceUrl != row.SourceUrl
            || issuer.RequestedIsin != row.Isin
            || !EquityMarketDirectoryGate.IsHomeShare(market, row, firds)
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
            LegalEntityIdentifier = confirmedIssuer.LegalEntityIdentifier,
            RelatedIsins = firdsIssuer == null ? issuer.RelatedIsins : [row.Isin],
            Isin = row.Isin,
            Ticker = row.Symbol,
            MarketIdentifierCode = row.MarketIdentifierCode,
            MarketCountryCode = market.CountryCode,
            TradingCurrency = quoted ? currency : null,
            QuoteUnitMultiplier = quoted ? multiplier : null,
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
                        firds.FirstTradeDate,
                        firds.TerminationDate,
                    },
                    Issuer = issuer,
                    FirdsIssuer = firdsIssuer,
                }
            ),
        };
    }
}
