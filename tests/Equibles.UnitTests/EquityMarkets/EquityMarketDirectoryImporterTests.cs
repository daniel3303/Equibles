using Equibles.CommonStocks.BusinessLogic.Directory;
using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Data;
using Equibles.EquityMarkets.BusinessLogic.Directory;
using Equibles.EquityMarkets.Data;
using Equibles.EquityMarkets.Data.Catalog;
using Equibles.EquityMarkets.Data.Models;
using Equibles.EquityMarkets.Repositories;
using Equibles.Integrations.Gleif;
using Equibles.Integrations.Gleif.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Equibles.UnitTests.EquityMarkets;

/// <summary>
/// Contract: a market pass waits for the FIRDS universe, reconciles the whole directory first, then
/// imports only rows the gate confirms as the market's own share listings, re-verifies an unchanged
/// listing monthly rather than daily, and counts a failed row without abandoning the rest.
/// </summary>
public class EquityMarketDirectoryImporterTests
{
    private const string Lei = "529900S21EQ1BO4ESM68";
    private static readonly EquityMarket Paris = EquityMarketCatalog.TryGet("euronext-paris");
    private static readonly Guid SnapshotId = Guid.NewGuid();

    private static readonly IModuleConfiguration[] Modules =
    [
        new CommonStocksModuleConfiguration(),
        new EquityMarketsModuleConfiguration(),
    ];

    // The in-memory store keys FirdsInstrumentRecord's composite primary key against the entity type of the
    // first model that touched it, so every context of a test must share one model.
    private static DbContextOptions<EquiblesFinancialDbContext> NewDbOptions()
    {
        var options = new DbContextOptionsBuilder<EquiblesFinancialDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString(), new InMemoryDatabaseRoot())
            .EnableServiceProviderCaching(false)
            .Options;
        using var prototype = new EquiblesFinancialDbContext(options, Modules);
        return new DbContextOptionsBuilder<EquiblesFinancialDbContext>(options)
            .UseModel(prototype.Model)
            .Options;
    }

    private static EquiblesFinancialDbContext NewContext(
        DbContextOptions<EquiblesFinancialDbContext> options
    )
    {
        var context = new EquiblesFinancialDbContext(options, Modules);
        context.Database.EnsureCreated();
        return context;
    }

    private static IServiceScopeFactory ScopeFactory(
        DbContextOptions<EquiblesFinancialDbContext> options
    )
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => NewContext(options));
        services.AddScoped<FirdsImportRunRepository>();
        services.AddScoped<FirdsInstrumentRecordRepository>();
        services.AddScoped<EquityListingRepository>();
        services.AddScoped<EquityDirectorySourceRecordRepository>();
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    private static async Task SeedFullImport(DbContextOptions<EquiblesFinancialDbContext> options)
    {
        using var context = NewContext(options);
        context.Add(
            new FirdsImportRun
            {
                Authority = "ESMA",
                Kind = FirdsFileKind.Full,
                FileName = "FULINS_E_20260912_01of01.zip",
                PublishedOn = new DateOnly(2026, 9, 12),
                ImportedAt = DateTime.UtcNow,
            }
        );
        await context.SaveChangesAsync();
    }

    private static async Task SeedFirds(
        DbContextOptions<EquiblesFinancialDbContext> options,
        string isin,
        string mic = "XPAR",
        string venue = "XPAR",
        string cfi = "ESVUFR",
        DateTime? terminated = null,
        string authority = "FR"
    )
    {
        using var context = NewContext(options);
        context.Add(
            new FirdsInstrumentRecord
            {
                Authority = "ESMA",
                Isin = isin,
                Mic = mic,
                Lei = Lei,
                Cfi = cfi,
                Currency = "EUR",
                FullName = isin,
                RelevantCompetentAuthority = authority,
                RelevantTradingVenue = venue,
                TerminationDate = terminated,
                ObservedAt = DateTime.UtcNow,
            }
        );
        await context.SaveChangesAsync();
    }

    private static async Task SeedVerifiedListing(
        DbContextOptions<EquiblesFinancialDbContext> options,
        EquityMarketDirectoryRow row,
        DateTime capturedAt,
        string ticker = null,
        string lei = Lei
    )
    {
        using var context = NewContext(options);
        var issuer = new EquityIssuer { Name = row.Name, LegalEntityIdentifier = lei };
        var security = new EquitySecurity { Issuer = issuer, Isin = row.Isin };
        issuer.Securities.Add(security);
        security.Listings.Add(
            new EquityListing
            {
                Security = security,
                Ticker = ticker ?? row.Symbol,
                MarketIdentifierCode = row.MarketIdentifierCode,
                MarketCountryCode = "FR",
                TradingCurrency = "EUR",
                QuoteUnitMultiplier = 1m,
                IdentityState = EquityIdentityState.Verified,
                IsDirectoryListed = true,
                IdentitySourceUrl = row.SourceUrl.AbsoluteUri,
            }
        );
        context.Add(issuer);
        context.Add(
            new EquityDirectorySourceRecord
            {
                Source = "euronext",
                SourceRecordKey = row.SourceUrl.AbsoluteUri,
                PayloadHash = Guid.NewGuid().ToString("N"),
                PayloadJson = "{}",
                CapturedAt = capturedAt,
            }
        );
        await context.SaveChangesAsync();
    }

    private static EquityMarketDirectoryRow Row(
        string isin,
        string symbol,
        string mic = "XPAR",
        string statedPrimaryMarket = null
    ) =>
        new()
        {
            Isin = isin,
            MarketIdentifierCode = mic,
            Symbol = symbol,
            Name = symbol + " SA",
            ReportedCurrency = "EUR",
            StatedPrimaryMarketIdentifierCode = statedPrimaryMarket,
            SourceUrl = new Uri($"https://live.euronext.com/en/product/equities/{isin}-{mic}"),
        };

    private static GleifIssuerIdentity Issuer(string isin, string lei = Lei) =>
        new()
        {
            RequestedIsin = isin,
            LegalEntityIdentifier = lei,
            LegalName = isin + " SE",
            EntityStatus = "ACTIVE",
            RegistrationStatus = "ISSUED",
            RelatedIsins = [isin],
        };

    private sealed record Harness(
        EquityMarketDirectoryImporter Importer,
        FakeSource Source,
        EquityDirectoryIdentityImporter Identity,
        EquityDirectorySnapshotManager Snapshots,
        GleifIdentityClient Gleif
    );

    private static Harness Build(
        DbContextOptions<EquiblesFinancialDbContext> options,
        params EquityMarketDirectoryRow[] rows
    ) => Build(options, "euronext", "euronext-paris", rows);

    private static Harness Build(
        DbContextOptions<EquiblesFinancialDbContext> options,
        string sourceKey,
        string marketCode,
        EquityMarketDirectoryRow[] rows
    )
    {
        var scopeFactory = ScopeFactory(options);
        var identity = Substitute.For<EquityDirectoryIdentityImporter>(scopeFactory);
        identity
            .ImportListing(Arg.Any<EquityDirectoryListingInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Guid.NewGuid()));
        var snapshots = Substitute.For<EquityDirectorySnapshotManager>(scopeFactory);
        snapshots
            .Reconcile(Arg.Any<EquityDirectorySnapshotInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(SnapshotId));
        var gleif = Substitute.For<GleifIdentityClient>(
            new HttpClient(),
            NullLogger<GleifIdentityClient>.Instance
        );
        gleif
            .GetIssuerForIsin(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(Issuer(call.Arg<string>())));
        var source = new FakeSource(rows, sourceKey, marketCode);
        var importer = new EquityMarketDirectoryImporter(
            [source],
            gleif,
            identity,
            snapshots,
            scopeFactory,
            Substitute.For<ILogger<EquityMarketDirectoryImporter>>()
        );
        return new Harness(importer, source, identity, snapshots, gleif);
    }

    [Fact]
    public async Task WithoutAFirdsFullImport_TheMarketPassWaitsAndTouchesNothing()
    {
        var options = NewDbOptions();
        var harness = Build(options, Row("FR0000120271", "TTE"));

        var result = await harness.Importer.Import(Paris, CancellationToken.None);

        result.Error.Should().Contain("FIRDS universe");
        result.Listings.Should().Be(0);
        harness.Source.Captures.Should().Be(0);
        await harness
            .Snapshots.DidNotReceive()
            .Reconcile(Arg.Any<EquityDirectorySnapshotInput>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheWholeDirectoryIsReconciled_ThenOnlyPrimaryVenueSharesAreImported()
    {
        var options = NewDbOptions();
        await SeedFullImport(options);
        await SeedFirds(options, "FR0000120271");
        await SeedFirds(options, "NL0000235190", venue: "XAMS");
        await SeedFirds(options, "FR0010451203", cfi: "EDCNFR");
        await SeedFirds(options, "FR0000121014", terminated: DateTime.UtcNow.AddDays(-1));
        var rows = new[]
        {
            Row("FR0000120271", "TTE"),
            Row("NL0000235190", "AIR"),
            Row("FR0010451203", "RXL"),
            Row("FR0000121014", "MC"),
            Row("FR0000120073", "AI"),
        };
        var harness = Build(options, rows);

        var result = await harness.Importer.Import(Paris, CancellationToken.None);

        result.Error.Should().BeNull();
        result.Listings.Should().Be(5);
        result.Imported.Should().Be(1);
        result
            .Skipped.Should()
            .Be(
                4,
                "a secondary venue, a receipt, a terminated line and an unknown ISIN are not primary-venue shares"
            );
        result.Failed.Should().Be(0);
        result.Current.Should().Be(0);
        harness.Source.Resolved.Should().Equal("FR0000120271");
        await harness
            .Snapshots.Received(1)
            .Reconcile(
                Arg.Is<EquityDirectorySnapshotInput>(input =>
                    input.Source == "euronext"
                    && input.EvidenceSource == "fake-euronext-directory-v1"
                    && input.SourceRecordKey == FakeSource.DirectoryUrl
                    && input.MarketCountryCode == "FR"
                    && input.MarketIdentifierCodes.SequenceEqual(Paris.MarketIdentifierCodes)
                    && input.Listings.Count == 5
                    && input.Listings.Any(key =>
                        key.Isin == "FR0000120073"
                        && key.Ticker == "AI"
                        && key.MarketIdentifierCode == "XPAR"
                    )
                ),
                Arg.Any<CancellationToken>()
            );
        await harness
            .Identity.Received(1)
            .ImportListing(
                Arg.Is<EquityDirectoryListingInput>(input =>
                    input.Isin == "FR0000120271"
                    && input.Ticker == "TTE"
                    && input.Source == "euronext"
                    && input.DirectorySnapshotId == SnapshotId
                    && input.LegalEntityIdentifier == Lei
                    && input.TradingCurrency == "EUR"
                    && input.QuoteUnitMultiplier == 1m
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task ARowEuronextHomesOnASiblingMarket_IsSkippedWithoutAnyLookup()
    {
        var options = NewDbOptions();
        await SeedFullImport(options);
        await SeedFirds(options, "BE0974278104", authority: "BE");
        var row = Row("BE0974278104", "ABO", "XPAR", "XBRU");
        row.SourceUrl = new Uri("https://live.euronext.com/en/product/equities/BE0974278104-XBRU");
        var harness = Build(options, row);

        var result = await harness.Importer.Import(Paris, CancellationToken.None);

        result.Error.Should().BeNull();
        result.Listings.Should().Be(1);
        result
            .Skipped.Should()
            .Be(
                1,
                "the directory homes the line on Brussels even where FIRDS files its relevant venue on Paris"
            );
        result.Imported.Should().Be(0);
        result.Failed.Should().Be(0);
        harness.Source.Resolved.Should().BeEmpty();
        await harness
            .Identity.DidNotReceive()
            .ImportListing(Arg.Any<EquityDirectoryListingInput>(), Arg.Any<CancellationToken>());
        await harness
            .Gleif.DidNotReceive()
            .GetIssuerForIsin(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AFailedRow_IsCountedAndTheRestOfTheMarketStillImports()
    {
        var options = NewDbOptions();
        await SeedFullImport(options);
        await SeedFirds(options, "FR0000120271");
        await SeedFirds(options, "FR0000120073");
        await SeedFirds(options, "FR0000125486");
        var harness = Build(
            options,
            Row("FR0000120271", "TTE"),
            Row("FR0000120073", "AI"),
            Row("FR0000125486", "DG")
        );
        harness
            .Gleif.GetIssuerForIsin("FR0000120073", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Issuer("FR0000120073", lei: "969500KLZPNMO8TUM811")));
        harness
            .Identity.ImportListing(
                Arg.Is<EquityDirectoryListingInput>(input => input.Isin == "FR0000125486"),
                Arg.Any<CancellationToken>()
            )
            .Returns<Task<Guid>>(_ =>
                throw new InvalidDataException("symbol owned by another listing")
            );

        var result = await harness.Importer.Import(Paris, CancellationToken.None);

        result.Imported.Should().Be(1);
        result.Failed.Should().Be(2, "a FIRDS/GLEIF issuer mismatch and a refused identity write");
        result.Skipped.Should().Be(0);
        harness.Source.Resolved.Should().Equal("FR0000120271", "FR0000120073", "FR0000125486");
    }

    [Fact]
    public async Task AVerifiedUnchangedListingCapturedThisMonth_IsCurrentWithoutAnyLookup()
    {
        var options = NewDbOptions();
        await SeedFullImport(options);
        await SeedFirds(options, "FR0000120271");
        var row = Row("FR0000120271", "TTE");
        await SeedVerifiedListing(options, row, DateTime.UtcNow.AddDays(-10));
        var harness = Build(options, row);

        var result = await harness.Importer.Import(Paris, CancellationToken.None);

        result.Current.Should().Be(1);
        result.Imported.Should().Be(0);
        harness.Source.Resolved.Should().BeEmpty();
        await harness
            .Gleif.DidNotReceive()
            .GetIssuerForIsin(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await harness
            .Identity.DidNotReceive()
            .ImportListing(Arg.Any<EquityDirectoryListingInput>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MissingLeiReopensRecentListingAndUsesExactFirdsRecovery()
    {
        var options = NewDbOptions();
        await SeedFullImport(options);
        await SeedFirds(options, "FR0000120271");
        var row = Row("FR0000120271", "TTE");
        await SeedVerifiedListing(options, row, DateTime.UtcNow.AddHours(-1), lei: null);
        var harness = Build(options, row);
        harness
            .Gleif.GetIssuerForIsin(row.Isin, Arg.Any<CancellationToken>())
            .Returns(new GleifIssuerIdentity { RequestedIsin = row.Isin });
        harness
            .Gleif.GetIssuerForLei(Lei, Arg.Any<CancellationToken>())
            .Returns(
                new GleifIssuerIdentity
                {
                    RequestedLei = Lei,
                    LegalEntityIdentifier = Lei,
                    EntityStatus = "ACTIVE",
                    RegistrationStatus = "ISSUED",
                }
            );
        var result = await harness.Importer.Import(Paris, CancellationToken.None);
        result.Current.Should().Be(0);
        result.Imported.Should().Be(1);
        result.Failed.Should().Be(0);
        await harness.Gleif.Received(1).GetIssuerForLei(Lei, Arg.Any<CancellationToken>());
        await harness
            .Identity.Received(1)
            .ImportListing(
                Arg.Is<EquityDirectoryListingInput>(input =>
                    input.LegalEntityIdentifier == Lei
                    && input.RelatedIsins.SequenceEqual(new[] { row.Isin })
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task PrimaryLookupFailureNeverFallsBackToAnotherIdentityRoute()
    {
        var options = NewDbOptions();
        await SeedFullImport(options);
        await SeedFirds(options, "FR0000120271");
        var row = Row("FR0000120271", "TTE");
        var harness = Build(options, row);
        harness
            .Gleif.GetIssuerForIsin(row.Isin, Arg.Any<CancellationToken>())
            .Returns<Task<GleifIssuerIdentity>>(_ =>
                throw new InvalidDataException("ambiguous identity")
            );
        var result = await harness.Importer.Import(Paris, CancellationToken.None);
        result.Failed.Should().Be(1);
        result.Imported.Should().Be(0);
        await harness
            .Gleif.DidNotReceive()
            .GetIssuerForLei(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AListingCapturedOverAMonthAgo_IsReverified()
    {
        var options = NewDbOptions();
        await SeedFullImport(options);
        await SeedFirds(options, "FR0000120271");
        var row = Row("FR0000120271", "TTE");
        await SeedVerifiedListing(
            options,
            row,
            DateTime.UtcNow
                - EquityMarketDirectoryImporter.ReverificationInterval
                - TimeSpan.FromHours(1)
        );
        var harness = Build(options, row);

        var result = await harness.Importer.Import(Paris, CancellationToken.None);

        result.Current.Should().Be(0);
        result.Imported.Should().Be(1);
        harness.Source.Resolved.Should().Equal("FR0000120271");
    }

    [Fact]
    public async Task AChangedSymbol_IsReverifiedEvenWhenCapturedYesterday()
    {
        var options = NewDbOptions();
        await SeedFullImport(options);
        await SeedFirds(options, "FR0000120271");
        var row = Row("FR0000120271", "TTE");
        await SeedVerifiedListing(options, row, DateTime.UtcNow.AddDays(-1), ticker: "FP");
        var harness = Build(options, row);

        var result = await harness.Importer.Import(Paris, CancellationToken.None);

        result.Current.Should().Be(0);
        result.Imported.Should().Be(1);
    }

    [Fact]
    public async Task AXetraRow_IsImportedOnlyWhenTheListAndFirdsAgreeItsHomeIsDeutscheBoerse()
    {
        var options = NewDbOptions();
        await SeedFullImport(options);
        await SeedFirds(options, "DE0007164600", mic: "XETA", venue: "FRAA", authority: "DE");
        await SeedFirds(options, "DE0005495626", mic: "XETB", venue: "MUNB", authority: "DE");
        await SeedFirds(options, "LU2818110020", mic: "XETA", venue: "XETA", authority: "LV");
        await SeedFirds(options, "ATFREQUENT09", mic: "XETA", venue: "WBAH", authority: "AT");
        await SeedFirds(options, "AT000000STR1", mic: "XETB", venue: "FRAB", authority: "DE");
        var rows = new[]
        {
            Row("DE0007164600", "SAP", "XETR", "XFRA"),
            Row("DE0005495626", "GME", "XETR", "FRAB"),
            Row("LU2818110020", "ELV", "XETR"),
            Row("ATFREQUENT09", "FQT", "XETR", "XFRA"),
            Row("AT000000STR1", "XD4", "XETR", "XWBO"),
        };
        var harness = Build(options, "xetra", "xetra", rows);

        var result = await harness.Importer.Import(
            EquityMarketCatalog.TryGet("xetra"),
            CancellationToken.None
        );

        result.Error.Should().BeNull();
        result.Listings.Should().Be(5);
        result.Imported.Should().Be(3);
        result
            .Skipped.Should()
            .Be(
                2,
                "a home FIRDS places in Austria and a Vienna-primary share are not Xetra's own listings"
            );
        result.Failed.Should().Be(0);
        harness.Source.Resolved.Should().Equal("DE0007164600", "DE0005495626", "LU2818110020");
        await harness
            .Identity.Received(1)
            .ImportListing(
                Arg.Is<EquityDirectoryListingInput>(input =>
                    input.Isin == "DE0007164600"
                    && input.Ticker == "SAP"
                    && input.Source == "xetra"
                    && input.MarketIdentifierCode == "XETR"
                    && input.MarketCountryCode == "DE"
                    && input.PayloadJson.Contains("\"StatedPrimaryMarketIdentifierCode\":\"XFRA\"")
                    && input.PayloadJson.Contains("\"RelevantTradingVenue\":\"FRAA\"")
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task AMarketWithoutAServingSource_IsRefusedLoudly()
    {
        var options = NewDbOptions();
        var harness = Build(options);
        var import = () =>
            harness.Importer.Import(EquityMarketCatalog.TryGet("xetra"), CancellationToken.None);
        await import.Should().ThrowAsync<InvalidOperationException>();
    }

    internal sealed class FakeSource(
        IReadOnlyList<EquityMarketDirectoryRow> rows,
        string sourceKey = "euronext",
        string marketCode = "euronext-paris"
    ) : IEquityMarketDirectorySource
    {
        public const string DirectoryUrl =
            "https://live.euronext.com/en/markets/paris/equities/list";

        public int Captures { get; private set; }
        public List<string> Resolved { get; } = [];

        public string SourceKey => sourceKey;

        public bool Supports(EquityMarket market) => market.Code == marketCode;

        public Task<EquityMarketDirectorySnapshot> Capture(
            EquityMarket market,
            CancellationToken cancellationToken
        )
        {
            Captures++;
            return Task.FromResult(
                new EquityMarketDirectorySnapshot
                {
                    EvidenceSource = "fake-" + sourceKey + "-directory-v1",
                    SourceUrl = new Uri(DirectoryUrl),
                    CapturedAt = DateTime.UtcNow,
                    PayloadJson = "{\"rows\":" + rows.Count + "}",
                    Rows = rows.ToList(),
                }
            );
        }

        public Task<EquityMarketDirectoryProduct> Resolve(
            EquityMarket market,
            EquityMarketDirectoryRow row,
            FirdsInstrumentRecord firds,
            CancellationToken cancellationToken
        )
        {
            Resolved.Add(row.Isin);
            return Task.FromResult(
                new EquityMarketDirectoryProduct
                {
                    SourceIssuerIdentifier = "issuer-" + row.Isin,
                    Name = row.Name,
                    SourceUrl = row.SourceUrl,
                    ReportedCurrency = row.ReportedCurrency,
                    Evidence = new { row.Isin },
                }
            );
        }
    }
}
