using System.Text.Json;
using Equibles.CommonStocks.BusinessLogic.Directory;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.AutoWiring;
using Equibles.Integrations.Euronext;
using Equibles.Integrations.Euronext.Models;
using Equibles.Integrations.Gleif;
using Equibles.Integrations.Gleif.Models;

namespace Equibles.CommonStocks.HostedService.Services;

[Service]
public class EuronextEquityDirectoryImporter(
    EuronextDirectoryClient directoryClient,
    GleifIdentityClient gleifClient,
    EquityDirectoryIdentityImporter identityImporter,
    IServiceScopeFactory scopeFactory,
    ILogger<EuronextEquityDirectoryImporter> logger
)
{
    public async Task<(int Imported, int Failed)> ImportLisbon(CancellationToken cancellationToken)
    {
        var token = cancellationToken;
        var snapshot = await directoryClient.GetLisbonEquities(token);
        await ArchiveDirectory(snapshot, token);
        var imported = 0;
        var failed = 0;
        foreach (var listing in snapshot.Listings)
        {
            using var attempt = CancellationTokenSource.CreateLinkedTokenSource(token);
            attempt.CancelAfter(TimeSpan.FromSeconds(30));
            try
            {
                var product = await directoryClient.GetInstrumentIdentity(listing, attempt.Token);
                var issuer = await gleifClient.GetIssuerForIsin(listing.Isin, attempt.Token);
                var input = CreateInput(listing, product, issuer);
                await identityImporter.ImportListing(input, attempt.Token);
                imported++;
            }
            catch (OperationCanceledException exception) when (!token.IsCancellationRequested)
            {
                failed++;
                logger.LogWarning(
                    exception,
                    "Lisbon identity lookup timed out for {Isin} on {Mic}; continuing with later records",
                    listing.Isin,
                    listing.MarketIdentifierCode
                );
            }
            catch (Exception exception)
                when (exception is InvalidDataException or HttpRequestException or JsonException)
            {
                failed++;
                logger.LogWarning(
                    exception,
                    "Lisbon directory identity remains unresolved for {Isin} on {Mic}",
                    listing.Isin,
                    listing.MarketIdentifierCode
                );
            }
        }
        logger.LogInformation(
            "Lisbon equity directory cycle complete: {Imported} imported, {Failed} unresolved, {Total} source listings",
            imported,
            failed,
            snapshot.Listings.Count
        );
        return (imported, failed);
    }

    internal static EquityDirectoryListingInput CreateInput(
        EuronextEquityListing listing,
        EuronextInstrumentIdentity product,
        GleifIssuerIdentity issuer
    )
    {
        if (
            EuronextDirectoryParser.ValidateProductUrl(listing) != product.SourceUrl
            || listing.Isin != product.Isin
            || listing.Symbol != product.Symbol
            || listing.MarketIdentifierCode != product.MarketIdentifierCode
            || issuer.RequestedIsin != listing.Isin
            || product.SourceInstrumentType != "STOCK"
        )
            throw new InvalidDataException(
                "Lisbon identity sources disagree on the requested security."
            );
        if (
            issuer.LegalEntityIdentifier != null
            && (
                issuer.EntityStatus != "ACTIVE"
                || issuer.RegistrationStatus is not ("ISSUED" or "LAPSED")
            )
        )
            throw new InvalidDataException("GLEIF issuer is not a current legal identity.");
        if (listing.ReportedCurrency is not (null or "EUR"))
            throw new InvalidDataException(
                "Lisbon quotation currency needs an explicit supported unit contract."
            );
        return new EquityDirectoryListingInput
        {
            Source = "euronext",
            SourceIssuerIdentifier = product.IssuerCode,
            IssuerName = issuer.LegalName ?? product.Name,
            LegalEntityIdentifier = issuer.LegalEntityIdentifier,
            RelatedIsins = issuer.RelatedIsins,
            Isin = listing.Isin,
            Ticker = listing.Symbol,
            MarketIdentifierCode = listing.MarketIdentifierCode,
            MarketCountryCode = "PT",
            TradingCurrency = listing.ReportedCurrency,
            QuoteUnitMultiplier = listing.ReportedCurrency == "EUR" ? 1m : null,
            SourceUrl = product.SourceUrl.AbsoluteUri,
            PayloadJson = JsonSerializer.Serialize(
                new
                {
                    Listing = listing,
                    Product = product,
                    Issuer = issuer,
                }
            ),
        };
    }

    private async Task ArchiveDirectory(EuronextDirectorySnapshot snapshot, CancellationToken token)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var issuers = scope.ServiceProvider.GetRequiredService<EquityIssuerRepository>();
        var evidence =
            scope.ServiceProvider.GetRequiredService<EquityDirectorySourceRecordRepository>();
        await using var transaction = await issuers.BeginDirectoryIdentityWrite(token);
        if (transaction == null)
            throw new InvalidOperationException(
                "Directory capture requires a relational transaction."
            );
        await evidence.Append(
            "euronext-lisbon-directory-v1",
            snapshot.SourceUrl.AbsoluteUri,
            JsonSerializer.Serialize(snapshot),
            token
        );
        await transaction.CommitAsync(token);
    }
}
