using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.CorporateActions.Repositories;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.BusinessLogic;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Statements;
using Equibles.Sec.FinancialFacts.Mcp.Tools;
using Equibles.Sec.FinancialFacts.Repositories;

namespace Equibles.IntegrationTests.Mcp;

/// <summary>
/// The fact tools read the net income a host proves for an issuer (<see cref="INetIncomeFactsReader"/>)
/// in place of the alias's tags. UBS's 2025 20-F tags total profit at 7,797M; the host proves the
/// owners' profit at 7,767M (7,797M less a 30M minority share).
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class FinancialFactsToolsHostNetIncomeTests : ParadeDbMcpTestBase
{
    private static readonly Guid OwnersProfit = Guid.NewGuid();

    public FinancialFactsToolsHostNetIncomeTests(ParadeDbFixture fixture)
        : base(fixture) { }

    private FinancialFactsTools Sut(INetIncomeFactsReader reader) =>
        new(
            new StatementFactRepository(DbContext),
            new FinancialConceptRepository(DbContext),
            new EquityIssuerRepository(DbContext),
            new StockSplitRepository(DbContext),
            ErrorManager,
            NullLogger<FinancialFactsTools>(),
            netIncomeFactsReader: reader
        );

    private sealed class Reader(Guid issuerId, decimal? value) : INetIncomeFactsReader
    {
        public Task<NetIncomeReading> Read(
            EquityIssuer issuer,
            CancellationToken cancellationToken = default
        ) =>
            Task.FromResult(
                issuer.Id != issuerId ? null
                : value == null
                    ? new NetIncomeReading(
                        [],
                        new Dictionary<Guid, FinancialConceptAliases.ConceptRef>()
                    )
                : new NetIncomeReading(
                    [Year(issuerId, OwnersProfit, value.Value)],
                    new Dictionary<Guid, FinancialConceptAliases.ConceptRef>
                    {
                        [OwnersProfit] = new(
                            FactTaxonomy.IfrsFull,
                            "ProfitLossAttributableToOwnersOfParent"
                        ),
                    }
                )
            );
    }

    private static FinancialFact Year(Guid issuerId, Guid conceptId, decimal value) =>
        new()
        {
            Id = Guid.NewGuid(),
            EquityIssuerId = issuerId,
            FinancialConceptId = conceptId,
            Unit = "USD",
            PeriodType = FactPeriodType.Duration,
            PeriodStart = new DateOnly(2025, 1, 1),
            PeriodEnd = new DateOnly(2025, 12, 31),
            Value = value,
            FiscalYear = 2025,
            FiscalPeriod = SecFiscalPeriod.FullYear,
            Form = DocumentType.TwentyF,
            FiledDate = new DateOnly(2026, 3, 9),
            AccessionNumber = "0001610520-26-000023",
        };

    private async Task<EquityIssuer> Seed()
    {
        var profit = new FinancialConcept
        {
            Id = Guid.NewGuid(),
            Taxonomy = FactTaxonomy.IfrsFull,
            Tag = "ProfitLoss",
            Label = "Profit (loss)",
        };
        var ubs = Equibles.TestSupport.EquityIssuerSeed.Create(
            Id: Guid.NewGuid(),
            Ticker: "UBS",
            Name: "UBS Group AG",
            Cik: "0001610520"
        );
        DbContext.Set<FinancialConcept>().Add(profit);
        DbContext.Set<EquityIssuer>().Add(ubs);
        DbContext.Set<FinancialFact>().Add(Year(ubs.Id, profit.Id, 7_797_000_000m));
        await DbContext.SaveChangesAsync();
        return ubs;
    }

    [Fact]
    public async Task GetFinancialFact_HostReadsTheNetIncome_ShowsTheHostsFigure()
    {
        var ubs = await Seed();

        var result = await Sut(new Reader(ubs.Id, 7_767_000_000m))
            .GetFinancialFact("UBS", "net-income");

        result.Should().Contain("7,767").And.NotContain("7,797");
    }

    [Fact]
    public async Task GetFinancialFact_HostShowsNoNetIncome_ShowsNoTaggedFigure()
    {
        var ubs = await Seed();

        var result = await Sut(new Reader(ubs.Id, null)).GetFinancialFact("UBS", "net-income");

        result.Should().NotContain("7,797");
    }

    [Fact]
    public async Task CompareFinancialFact_HostReadsTheNetIncome_ComparesTheHostsFigure()
    {
        var ubs = await Seed();

        var result = await Sut(new Reader(ubs.Id, 7_767_000_000m))
            .CompareFinancialFact(["UBS"], "net-income", fiscalYear: 2025, fiscalPeriod: "FY");

        result.Should().Contain("7,767").And.NotContain("7,797");
    }

    [Fact]
    public async Task GetFinancialFact_HostHasNoReading_ShowsTheTag()
    {
        await Seed();

        var result = await Sut(new Reader(Guid.NewGuid(), 1m))
            .GetFinancialFact("UBS", "net-income");

        result.Should().Contain("7,797");
    }
}
