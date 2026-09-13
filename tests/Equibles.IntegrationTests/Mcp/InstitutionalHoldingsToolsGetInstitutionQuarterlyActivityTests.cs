using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.CorporateActions.Repositories;
using Equibles.Holdings.BusinessLogic;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Mcp.Tools;
using Equibles.Holdings.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Equibles.IntegrationTests.Mcp;

/// <summary>
/// Pins <c>GetInstitutionQuarterlyActivity</c>. Each test exercises one path: unknown
/// institution, single-quarter holder (no diff possible), all-four-buckets diff, and
/// the explicit <c>bucket</c> filter that limits the response to one section.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class InstitutionalHoldingsToolsGetInstitutionQuarterlyActivityTests : ParadeDbMcpTestBase
{
    public InstitutionalHoldingsToolsGetInstitutionQuarterlyActivityTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task GetInstitutionQuarterlyActivity_UnknownInstitution_ReportsNotFound()
    {
        await using var verify = Fixture.CreateDbContext();
        var sut = NewSut(verify);

        var output = await sut.GetInstitutionQuarterlyActivity("Definitely Not A Fund");

        output
            .Should()
            .Contain("No match for 'Definitely Not A Fund' in the tracked 13F filer set");
    }

    [Fact]
    public async Task GetInstitutionQuarterlyActivity_SingleQuarterHolder_ReportsTooFewQuarters()
    {
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "AAPL",
            Name: "Apple Inc.",
            Cik: "0000320193"
        );
        var holder = new InstitutionalHolder { Cik = "Q00010001", Name = "Single Quarter LP" };
        DbContext.AddRange(stock, holder);
        DbContext.Add(
            MakeHolding(stock, holder, new DateOnly(2024, 12, 31), shares: 1_000, value: 1_000_000)
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await using var verify = Fixture.CreateDbContext();
        var sut = NewSut(verify);

        var output = await sut.GetInstitutionQuarterlyActivity("Single Quarter");

        output.Should().Contain("fewer than two reported quarters");
    }

    [Fact]
    public async Task GetInstitutionQuarterlyActivity_TwoQuartersWithMovement_RendersAllSections()
    {
        EquityIssuer aapl = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "AAPL",
            Name: "Apple Inc.",
            Cik: "0000320193"
        );
        EquityIssuer msft = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "MSFT",
            Name: "Microsoft Corp.",
            Cik: "0000789019"
        );
        EquityIssuer nvda = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "NVDA",
            Name: "NVIDIA Corp.",
            Cik: "0001045810"
        );
        EquityIssuer tsla = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "TSLA",
            Name: "Tesla Inc.",
            Cik: "0001318605"
        );
        var holder = new InstitutionalHolder { Cik = "Q00010002", Name = "Active Allocator LP" };
        DbContext.AddRange(aapl, msft, nvda, tsla, holder);
        var prior = new DateOnly(2024, 9, 30);
        var current = new DateOnly(2024, 12, 31);
        // Same shape as #1013 view test: AAPL increased, MSFT reduced, NVDA initiated, TSLA exited.
        DbContext.Add(MakeHolding(aapl, holder, prior, shares: 1_000, value: 1_000_000));
        DbContext.Add(MakeHolding(msft, holder, prior, shares: 500, value: 500_000));
        DbContext.Add(MakeHolding(tsla, holder, prior, shares: 100, value: 100_000));
        DbContext.Add(MakeHolding(aapl, holder, current, shares: 1_500, value: 1_500_000));
        DbContext.Add(MakeHolding(msft, holder, current, shares: 200, value: 200_000));
        DbContext.Add(MakeHolding(nvda, holder, current, shares: 50, value: 50_000));
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await using var verify = Fixture.CreateDbContext();
        var sut = NewSut(verify);

        var output = await sut.GetInstitutionQuarterlyActivity("Active Allocator");

        output.Should().Contain("Quarterly activity — **Active Allocator LP**");
        output.Should().Contain("## Initiated");
        output.Should().Contain("## Increased");
        output.Should().Contain("## Reduced");
        output.Should().Contain("## Exited");
        output.Should().Contain("NVDA"); // initiated
        output.Should().Contain("AAPL"); // increased
        output.Should().Contain("MSFT"); // reduced
        output.Should().Contain("TSLA"); // exited
    }

    [Fact]
    public async Task GetInstitutionQuarterlyActivity_BucketFilter_LimitsToOneSection()
    {
        EquityIssuer aapl = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "AAPL",
            Name: "Apple Inc.",
            Cik: "0000320193"
        );
        EquityIssuer msft = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "MSFT",
            Name: "Microsoft Corp.",
            Cik: "0000789019"
        );
        var holder = new InstitutionalHolder { Cik = "Q00010003", Name = "Filtered LP" };
        DbContext.AddRange(aapl, msft, holder);
        var prior = new DateOnly(2024, 9, 30);
        var current = new DateOnly(2024, 12, 31);
        DbContext.Add(MakeHolding(aapl, holder, prior, shares: 1_000, value: 1_000_000));
        DbContext.Add(MakeHolding(aapl, holder, current, shares: 1_500, value: 1_500_000));
        DbContext.Add(MakeHolding(msft, holder, current, shares: 100, value: 100_000));
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await using var verify = Fixture.CreateDbContext();
        var sut = NewSut(verify);

        var output = await sut.GetInstitutionQuarterlyActivity("Filtered LP", bucket: "increased");

        output.Should().Contain("## Increased");
        output.Should().NotContain("## Initiated");
        output.Should().NotContain("## Reduced");
        output.Should().NotContain("## Exited");
        output.Should().Contain("AAPL");
        output.Should().NotContain("MSFT");
    }

    [Fact]
    public async Task GetInstitutionQuarterlyActivity_UnknownBucket_ReportsValidValues()
    {
        await using var verify = Fixture.CreateDbContext();
        var sut = NewSut(verify);

        var output = await sut.GetInstitutionQuarterlyActivity("Anything", bucket: "garbage");

        output.Should().Contain("Unknown bucket");
        output.Should().Contain("initiated");
        output.Should().Contain("exited");
    }

    private InstitutionalHoldingsTools NewSut(Equibles.Data.EquiblesFinancialDbContext ctx) =>
        new(
            new InstitutionalHoldingRepository(ctx),
            new InstitutionalHolderRepository(ctx),
            new EquityIssuerRepository(ctx),
            new StockSplitRepository(ctx),
            new StockCombinedQuarterService(
                new InstitutionalHoldingRepository(ctx),
                new StockSplitRepository(ctx)
            ),
            ErrorManager,
            Substitute.For<ILogger<InstitutionalHoldingsTools>>()
        );

    private static InstitutionalHolding MakeHolding(
        EquityIssuer stock,
        InstitutionalHolder holder,
        DateOnly reportDate,
        long shares,
        long value
    ) =>
        new()
        {
            EquityIssuerId = stock.Id,
            InstitutionalHolderId = holder.Id,
            FilingDate = reportDate.AddDays(45),
            ReportDate = reportDate,
            Shares = shares,
            Value = value,
            ShareType = ShareType.Shares,
            InvestmentDiscretion = InvestmentDiscretion.Sole,
            AccessionNumber = $"acc-{holder.Cik}-{reportDate:yyyyMMdd}",
        };
}
