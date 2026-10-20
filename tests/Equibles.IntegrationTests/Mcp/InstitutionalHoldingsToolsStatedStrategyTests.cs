using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.CorporateActions.Repositories;
using Equibles.Holdings.BusinessLogic;
using Equibles.Holdings.BusinessLogic.Contracts;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Mcp.Tools;
using Equibles.Holdings.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Equibles.IntegrationTests.Mcp;

/// <summary>
/// GetInstitutionSummary shows a stated strategy only when a deployment registers a source and it
/// returns one; without a source the summary is unchanged.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class InstitutionalHoldingsToolsStatedStrategyTests : ParadeDbMcpTestBase
{
    private static readonly DateOnly ReportDate = new(2024, 12, 31);

    public InstitutionalHoldingsToolsStatedStrategyTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task GetInstitutionSummary_NoSourceRegistered_ShowsNoStatedStrategy()
    {
        await SeedHolder("00090001", "Quiet Capital LP");
        await using var verify = Fixture.CreateDbContext();

        var output = await Sut(verify, statements: null).GetInstitutionSummary("Quiet Capital");

        output.Should().Contain("Portfolio summary — **Quiet Capital LP**");
        output.Should().NotContain("Stated strategy");
    }

    [Fact]
    public async Task GetInstitutionSummary_SourceHasNothingOnFile_ShowsNoStatedStrategy()
    {
        await SeedHolder("00090002", "Silent Partners LP");
        var source = Substitute.For<IHolderStrategyStatements>();
        source
            .Get(Arg.Any<InstitutionalHolder>(), Arg.Any<CancellationToken>())
            .Returns((HolderStatedStrategy)null);
        await using var verify = Fixture.CreateDbContext();

        var output = await Sut(verify, source).GetInstitutionSummary("Silent Partners");

        output.Should().Contain("Portfolio summary — **Silent Partners LP**");
        output.Should().NotContain("Stated strategy");
    }

    [Fact]
    public async Task GetInstitutionSummary_SourceReturnsStatements_RendersEachWithDateAndLink()
    {
        await SeedHolder("00090003", "Systematic Advisors LLC");
        var source = Substitute.For<IHolderStrategyStatements>();
        source
            .Get(
                Arg.Is<InstitutionalHolder>(holder => holder.Cik == "00090003"),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                new HolderStatedStrategy(
                    "Form ADV Part 2A",
                    new DateOnly(2026, 3, 31),
                    "https://example.org/brochure/1038771",
                    [
                        new HolderStrategyStatement(
                            "Quantitative / systematic",
                            "We employ systematic,\nmodel-driven investment strategies."
                        ),
                        new HolderStrategyStatement(
                            "Long/short hedge",
                            "Certain strategies take long and short positions."
                        ),
                    ]
                )
            );
        await using var verify = Fixture.CreateDbContext();

        var output = await Sut(verify, source).GetInstitutionSummary("Systematic Advisors");

        output
            .Should()
            .Contain("**Stated strategy (Form ADV Part 2A, 2026-03-31)**")
            .And.Contain(
                "- Quantitative / systematic: \"We employ systematic, model-driven investment strategies.\""
            )
            .And.Contain(
                "- Long/short hedge: \"Certain strategies take long and short positions.\""
            )
            .And.Contain("_Source: https://example.org/brochure/1038771_");
        output
            .IndexOf("Stated strategy", StringComparison.Ordinal)
            .Should()
            .BeLessThan(output.IndexOf("**Style**", StringComparison.Ordinal));
    }

    private async Task SeedHolder(string cik, string name)
    {
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: $"T{cik[^4..]}",
            Name: $"Stock {cik}",
            Cik: $"77{cik}"
        );
        var holder = new InstitutionalHolder { Cik = cik, Name = name };
        DbContext.AddRange(stock, holder);
        DbContext.Add(
            new InstitutionalHolding
            {
                EquityIssuerId = stock.Id,
                InstitutionalHolderId = holder.Id,
                FilingDate = ReportDate.AddDays(45),
                ReportDate = ReportDate,
                Shares = 1_000,
                Value = 1_000_000,
                ShareType = ShareType.Shares,
                InvestmentDiscretion = InvestmentDiscretion.Sole,
                AccessionNumber = $"acc-{cik}",
            }
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
    }

    private InstitutionalHoldingsTools Sut(
        Equibles.Data.EquiblesFinancialDbContext ctx,
        IHolderStrategyStatements statements
    ) =>
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
            Substitute.For<ILogger<InstitutionalHoldingsTools>>(),
            strategyStatements: statements == null ? null : [statements]
        );
}
