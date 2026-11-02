using System.Text.Json;
using Equibles.CommonStocks.BusinessLogic.Merge;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.CorporateActions.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Yahoo.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.IntegrationTests.CommonStocks;

/// <summary>
/// A holding-company reorganization that reached the directory before its successor-issuer report
/// left two issuers: the predecessor, withdrawn but holding the history, and an empty shell under the
/// new CIK holding the live listing. The fold joins them on the predecessor's row.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class EquityIssuerSuccessorFoldTests(ParadeDbFixture fixture) : ParadeDbMcpTestBase(fixture)
{
    private const string Ticker = "SUCC";
    private const string PredecessorCik = "0001593538";
    private const string SuccessorCik = "0002152623";
    private const string OldCusip = "63938C108";
    private const string NewCusip = "63938D106";
    private const string NewIsin = "US63938D1063";
    private static readonly DateOnly Day1 = new(2026, 10, 27);
    private static readonly DateOnly Day2 = new(2026, 10, 28);
    private static readonly DateOnly Day3 = new(2026, 10, 29);
    private static readonly DateOnly Day4 = new(2026, 10, 30);

    private sealed record Pair(
        Guid Predecessor,
        Guid PredecessorListing,
        Guid PredecessorSecurity,
        Guid Shell,
        Guid ShellListing,
        Guid ShellSecurity,
        Guid Evidence
    );

    private EquityIssuerMergeManager Manager() =>
        new(DbContext, new EquityIssuerRepository(DbContext), []);

    private static EquityIssuerMergeRequest Request(
        Pair pair,
        bool execute = true,
        bool successorFold = true
    ) =>
        new()
        {
            RetiredIssuerId = pair.Shell,
            SurvivingIssuerId = pair.Predecessor,
            EvidenceKind = "sec-successor-registration",
            EvidenceSourceRecordIds = [pair.Evidence],
            MergedBy = "successor-issuer-repair",
            Execute = execute,
            SuccessorFold = successorFold,
        };

    private static EquityDailyStockPrice Price(Guid listing, DateOnly date, decimal close) =>
        new()
        {
            EquityListingId = listing,
            SourceTicker = Ticker,
            Date = date,
            Open = close,
            High = close,
            Low = close,
            Close = close,
            AdjustedClose = close,
            Volume = 1000,
        };

    private static CashDividend Dividend(
        Guid issuer,
        Guid listing,
        DateOnly exDate,
        decimal amount,
        string currency = "USD"
    ) =>
        new()
        {
            EquityIssuerId = issuer,
            EquityListingId = listing,
            Currency = currency,
            ExDate = exDate,
            AmountPerShare = amount,
            Source = CashDividendSource.Yahoo,
        };

    // The predecessor's listing is a legacy row (no venue code, old CUSIP, no ISIN); the shell's came
    // from the directory with the successor's new CUSIP and ISIN, a venue code and a source identifier.
    private async Task<Pair> SeedSplitPair(
        string shellCik = SuccessorCik,
        string shellTicker = Ticker,
        string shellCurrency = "USD"
    )
    {
        var predecessor = Equibles.TestSupport.EquityIssuerSeed.Create(
            Cik: PredecessorCik,
            Ticker: Ticker,
            Name: "Navient Corp",
            Active: false,
            Website: "https://navient.com",
            Cusip: OldCusip,
            SecondaryTickers: ["SUCN"]
        );
        predecessor.Presentation.Listing.TradingCurrency = "USD";
        predecessor.SecRegistrantProfileReadOn = new DateOnly(2026, 10, 31);
        var shell = Equibles.TestSupport.EquityIssuerSeed.Create(
            Cik: shellCik,
            Ticker: shellTicker,
            Name: "Navient Holdings, Inc.",
            EntityType: "other",
            Sic: "6211",
            Cusip: NewCusip,
            Isin: NewIsin
        );
        shell.Presentation.Listing.TradingCurrency = shellCurrency;
        shell.Presentation.Listing.MarketIdentifierCode = "XNAS";
        shell.Presentation.Listing.QuoteUnitMultiplier = 1m;
        shell.Presentation.Listing.IdentityState = EquityIdentityState.Verified;
        shell.Presentation.Listing.IdentitySourceUrl = "https://example.org/directory";
        var evidence = new EquityDirectorySourceRecord
        {
            Source = "sec-successor-registration",
            SourceRecordKey = "0001193125-26-436899",
            PayloadHash = Guid.NewGuid().ToString("N"),
            PayloadJson = "{}",
        };
        DbContext.AddRange(predecessor, shell, evidence);
        await DbContext.SaveChangesAsync();
        var predecessorListing = predecessor.Presentation.EquityListingId;
        var shellListing = shell.Presentation.EquityListingId;
        var predecessorSecurity = predecessor.Presentation.Listing.EquitySecurityId;
        var shellSecurity = shell.Presentation.Listing.EquitySecurityId;
        DbContext.AddRange(
            new EquitySecuritySourceIdentifier
            {
                EquitySecurityId = shellSecurity,
                Source = "sec-directory",
                Identifier = NewIsin,
                SourceRecordId = evidence.Id,
            },
            // A filing already cited the new CUSIP, which import resolution aliased to the predecessor.
            new EquityIssuerCusipAlias { EquityIssuerId = predecessor.Id, Cusip = NewCusip }
        );

        var concept = new FinancialConcept { Taxonomy = FactTaxonomy.UsGaap, Tag = "Revenues" };
        DbContext.Add(concept);
        DbContext.AddRange(
            Price(predecessorListing, Day1, 1m),
            Price(predecessorListing, Day2, 2m),
            Price(predecessorListing, Day3, 3m),
            Price(shellListing, Day2, 20m),
            Price(shellListing, Day3, 30m),
            Price(shellListing, Day4, 40m),
            Dividend(predecessor.Id, predecessorListing, Day1, 0.16m),
            Dividend(shell.Id, shellListing, Day1, 0.16m, shellCurrency),
            Dividend(shell.Id, shellListing, Day4, 0.17m, shellCurrency),
            new FailToDeliver
            {
                EquityListingId = shellListing,
                ListedTicker = Ticker,
                SettlementDate = Day4,
                Quantity = 1000,
                Price = 10m,
            },
            new CompanyFilingSyncState
            {
                EquityIssuerId = predecessor.Id,
                LastSyncedAt = new DateTime(2026, 10, 21, 0, 0, 0, DateTimeKind.Utc),
            },
            new CompanyFilingSyncState
            {
                EquityIssuerId = shell.Id,
                LastSyncedAt = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
            }
        );
        await DbContext.SaveChangesAsync();
        DbContext.Add(
            new FinancialFact
            {
                EquityIssuerId = shell.Id,
                FinancialConceptId = concept.Id,
                Unit = "USD",
                Value = 1m,
                PeriodType = FactPeriodType.Duration,
                PeriodStart = new DateOnly(2026, 7, 1),
                PeriodEnd = new DateOnly(2026, 9, 30),
                FiscalYear = 2026,
                FiscalPeriod = SecFiscalPeriod.Q3,
                Form = DocumentType.TenQ,
                FiledDate = new DateOnly(2026, 10, 30),
                AccessionNumber = "0001193125-26-437084",
                DimensionsKey = "",
            }
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return new Pair(
            predecessor.Id,
            predecessorListing,
            predecessorSecurity,
            shell.Id,
            shellListing,
            shellSecurity,
            evidence.Id
        );
    }

    [Fact]
    public async Task TheShellFoldsIntoThePredecessor_WhichContinuesUnderTheSuccessorCik()
    {
        var pair = await SeedSplitPair();

        var report = await Manager().Merge(Request(pair));

        report.Refusals.Should().BeEmpty();
        report.Executed.Should().BeTrue();
        DbContext.ChangeTracker.Clear();
        (await DbContext.Set<EquityIssuer>().AnyAsync(row => row.Id == pair.Shell))
            .Should()
            .BeFalse();
        var issuer = await DbContext
            .Set<EquityIssuer>()
            .Include(row => row.Presentation)
            .SingleAsync(row => row.Id == pair.Predecessor);
        issuer.Cik.Should().Be(SuccessorCik);
        issuer.SecondaryCiks.Should().Equal(PredecessorCik);
        issuer.Name.Should().Be("Navient Holdings, Inc.");
        issuer.NameAliases.Should().Contain("Navient Corp");
        issuer.EntityType.Should().Be("other");
        issuer.Sic.Should().Be("6211");
        issuer.Website.Should().Be("https://navient.com");
        issuer
            .SecRegistrantProfileReadOn.Should()
            .BeNull("the profile is re-read under the new CIK");
        issuer.Presentation.EquityListingId.Should().Be(pair.PredecessorListing);

        var listings = await DbContext
            .Set<EquityListing>()
            .Where(row => row.Security.EquityIssuerId == issuer.Id)
            .ToListAsync();
        listings
            .Where(row => row.Ticker == Ticker)
            .Should()
            .ContainSingle()
            .Which.Id.Should()
            .Be(pair.PredecessorListing);
        var listing = listings.Single(row => row.Id == pair.PredecessorListing);
        listing.Active.Should().BeTrue();
        listing.IsDirectoryListed.Should().BeTrue();
        listing.DelistedOn.Should().BeNull();
        listing.PriceHistoryBackfilled.Should().BeFalse("the series changed");
        (await DbContext.Set<EquityListing>().AnyAsync(row => row.Id == pair.ShellListing))
            .Should()
            .BeFalse();
        (await DbContext.Set<EquitySecurity>().CountAsync(row => row.EquityIssuerId == issuer.Id))
            .Should()
            .Be(2, "the predecessor's two securities stay and the shell's empty one goes");
        listing.MarketIdentifierCode.Should().Be("XNAS", "the venue identity is the shell's");
        listing.IdentityState.Should().Be(EquityIdentityState.Verified);
        listing.IdentitySourceUrl.Should().Be("https://example.org/directory");
        listing.TradingCurrency.Should().Be("USD");
        var security = await DbContext
            .Set<EquitySecurity>()
            .SingleAsync(row => row.Id == pair.PredecessorSecurity);
        security
            .Cusip.Should()
            .Be(NewCusip, "the registrant's current CUSIP identifies the security");
        security.Isin.Should().Be(NewIsin);
        (await DbContext.Set<EquitySecurity>().AnyAsync(row => row.Id == pair.ShellSecurity))
            .Should()
            .BeFalse();
        (await DbContext.Set<EquitySecuritySourceIdentifier>().SingleAsync())
            .EquitySecurityId.Should()
            .Be(pair.PredecessorSecurity);
        (
            await DbContext
                .Set<EquityIssuerCusipAlias>()
                .Where(row => row.EquityIssuerId == issuer.Id)
                .Select(row => row.Cusip)
                .ToListAsync()
        )
            .Should()
            .Equal([OldCusip], "filings keep citing the old CUSIP, and the new one is no alias");

        var prices = await DbContext
            .Set<EquityDailyStockPrice>()
            .OrderBy(row => row.Date)
            .ToListAsync();
        prices
            .Select(row => row.EquityListingId)
            .Should()
            .AllBeEquivalentTo(pair.PredecessorListing);
        prices
            .Select(row => (row.Date, row.Close))
            .Should()
            .Equal(
                [(Day1, 1m), (Day2, 2m), (Day3, 3m), (Day4, 40m)],
                "overlapping days keep the predecessor's rows and new days follow"
            );
        var dividends = await DbContext
            .Set<CashDividend>()
            .OrderBy(row => row.ExDate)
            .ToListAsync();
        dividends
            .Select(row => (row.ExDate, row.EquityListingId, row.EquityIssuerId))
            .Should()
            .Equal([
                (Day1, (Guid?)pair.PredecessorListing, issuer.Id),
                (Day4, (Guid?)pair.PredecessorListing, issuer.Id),
            ]);
        (await DbContext.Set<FailToDeliver>().SingleAsync())
            .EquityListingId.Should()
            .Be(pair.PredecessorListing);
        (await DbContext.Set<FinancialFact>().SingleAsync()).EquityIssuerId.Should().Be(issuer.Id);
        (await DbContext.Set<CompanyFilingSyncState>().SingleAsync())
            .EquityIssuerId.Should()
            .Be(issuer.Id);

        var audit = await DbContext.Set<EquityIssuerMerge>().SingleAsync();
        audit.RetiredIssuerId.Should().Be(pair.Shell);
        audit.SurvivingIssuerId.Should().Be(pair.Predecessor);
        var stored = JsonSerializer.Deserialize<EquityIssuerMergeReport>(audit.ReportJson);
        stored
            .Remaps.Should()
            .ContainSingle(remap =>
                remap.Table == "EquityListing"
                && remap.RetiredKey == pair.ShellListing
                && remap.SurvivorKey == pair.PredecessorListing
            );
        stored.Remaps.Should().NotContain(remap => remap.Table == "EquityDailyStockPrice");
        report
            .Changes.Should()
            .Contain(change =>
                change.Table == "EquityDailyStockPrice"
                && change.Moved == 1
                && change.DroppedDuplicates == 2
            );
    }

    [Fact]
    public async Task APreviewOfTheFoldWritesNothing()
    {
        var pair = await SeedSplitPair();

        var report = await Manager().Merge(Request(pair, execute: false));

        report.IsClean.Should().BeTrue(string.Join(" ", report.Refusals));
        report.Executed.Should().BeFalse();
        report.Remaps.Should().ContainSingle(remap => remap.Table == "EquityListing");
        DbContext.ChangeTracker.Clear();
        (await DbContext.Set<EquityIssuer>().SingleAsync(row => row.Id == pair.Predecessor))
            .Cik.Should()
            .Be(PredecessorCik);
        (await DbContext.Set<EquityListing>().AnyAsync(row => row.Id == pair.ShellListing))
            .Should()
            .BeTrue();
        (await DbContext.Set<EquityDailyStockPrice>().CountAsync()).Should().Be(6);
        (await DbContext.Set<EquityIssuerMerge>().AnyAsync()).Should().BeFalse();
    }

    [Theory]
    [InlineData("shell-without-cik")]
    [InlineData("ticker-not-on-survivor")]
    [InlineData("plain-merge")]
    [InlineData("currency-conflict")]
    public async Task AFoldNeedsTwoFilersSharingTheTicker(string defect)
    {
        var pair = await SeedSplitPair(
            shellCik: defect == "shell-without-cik" ? null : SuccessorCik,
            shellTicker: defect == "ticker-not-on-survivor" ? "OTHR" : Ticker,
            shellCurrency: defect == "currency-conflict" ? "CAD" : "USD"
        );

        var report = await Manager().Merge(Request(pair, successorFold: defect != "plain-merge"));

        report.Refusals.Should().NotBeEmpty();
        report.Executed.Should().BeFalse();
        DbContext.ChangeTracker.Clear();
        (await DbContext.Set<EquityIssuer>().AnyAsync(row => row.Id == pair.Shell))
            .Should()
            .BeTrue();
        (await DbContext.Set<EquityIssuer>().SingleAsync(row => row.Id == pair.Predecessor))
            .Cik.Should()
            .Be(PredecessorCik);
    }
}
