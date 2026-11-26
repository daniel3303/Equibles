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

    private static readonly Dictionary<Guid, FinancialConceptAliases.ConceptRef> Concepts = new()
    {
        [OwnersProfit] = new(FactTaxonomy.IfrsFull, "ProfitLossAttributableToOwnersOfParent"),
    };

    private sealed class Reader(
        Guid issuerId,
        IReadOnlyList<FinancialFact> facts,
        IReadOnlySet<FinancialFact> derived = null
    ) : INetIncomeFactsReader
    {
        public Reader(Guid issuerId, decimal? value)
            : this(issuerId, value == null ? [] : [Year(issuerId, OwnersProfit, value.Value)]) { }

        public Task<NetIncomeReading> Read(
            EquityIssuer issuer,
            CancellationToken cancellationToken = default
        ) =>
            Task.FromResult(
                issuer.Id != issuerId
                    ? null
                    : new NetIncomeReading(facts, Concepts, derived ?? new HashSet<FinancialFact>())
            );
    }

    // Made-up 2025 quarters beside the proven year: 1,692M, 2,395M and 2,482M, so the year less
    // them is 1,198M.
    private static FinancialFact Quarter(
        Guid issuerId,
        SecFiscalPeriod period,
        int startMonth,
        decimal value,
        bool derived = false
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            EquityIssuerId = issuerId,
            FinancialConceptId = OwnersProfit,
            Unit = "USD",
            PeriodType = FactPeriodType.Duration,
            PeriodStart = new DateOnly(2025, startMonth, 1),
            PeriodEnd = new DateOnly(2025, startMonth, 1).AddMonths(3).AddDays(-1),
            Value = value,
            FiscalYear = 2025,
            FiscalPeriod = period,
            Form = derived ? null : DocumentType.SixK,
            FiledDate = new DateOnly(2025, startMonth, 1).AddMonths(4),
            AccessionNumber = derived ? null : $"0001610520-25-00{startMonth:00}01",
        };

    private static List<FinancialFact> ThreeQuarters(Guid issuerId) =>
        [
            Year(issuerId, OwnersProfit, 7_767_000_000m),
            Quarter(issuerId, SecFiscalPeriod.Q1, 1, 1_692_000_000m),
            Quarter(issuerId, SecFiscalPeriod.Q2, 4, 2_395_000_000m),
            Quarter(issuerId, SecFiscalPeriod.Q3, 7, 2_482_000_000m),
        ];

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

    [Fact]
    public async Task GetFinancialFact_HostHoldsAFourthQuarterBack_WorksOutNoneOfItsOwn()
    {
        var ubs = await Seed();

        var result = await Sut(new Reader(ubs.Id, ThreeQuarters(ubs.Id)))
            .GetFinancialFact("UBS", "net-income", fiscalPeriod: "Q4");

        result.Should().NotContain("1,198");
    }

    [Fact]
    public async Task GetFinancialFact_HostWorkedOutAFourthQuarter_ShowsItMarked()
    {
        var ubs = await Seed();
        var fourth = Quarter(ubs.Id, SecFiscalPeriod.Q4, 10, 1_198_000_000m, derived: true);

        var result = await Sut(
                new Reader(
                    ubs.Id,
                    [.. ThreeQuarters(ubs.Id), fourth],
                    new HashSet<FinancialFact> { fourth }
                )
            )
            .GetFinancialFact("UBS", "net-income", fiscalPeriod: "Q4");

        result.Should().Contain("1,198").And.Contain("(derived)");
    }

    [Fact]
    public async Task CompareFinancialFact_HostWorkedOutAFourthQuarter_ComparesFiledFiguresOnly()
    {
        var ubs = await Seed();
        var fourth = Quarter(ubs.Id, SecFiscalPeriod.Q4, 10, 1_198_000_000m, derived: true);

        var result = await Sut(
                new Reader(
                    ubs.Id,
                    [.. ThreeQuarters(ubs.Id), fourth],
                    new HashSet<FinancialFact> { fourth }
                )
            )
            .CompareFinancialFact(["UBS"], "net-income", fiscalYear: 2025, fiscalPeriod: "Q4");

        result.Should().NotContain("1,198");
    }
}
