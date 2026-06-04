using Equibles.CommonStocks.Data.Models;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;

namespace Equibles.IntegrationTests.Holdings;

/// <summary>
/// GH (commercial) #1542: the Double-Down Report was dominated by positions whose
/// prior quarter held 1 share (~$0 value), turning ordinary new positions into
/// "+190,898,100%" artifacts that buried genuine conviction increases. A double
/// down requires a non-trivial existing position — degenerate prior bases below
/// MinDoubleDownPreviousValue must be excluded from the report.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class InstitutionalHoldingRepositoryDoubleDownDegeneratePriorTests : IAsyncLifetime
{
    private readonly ParadeDbFixture _fixture;
    private readonly List<Equibles.Data.EquiblesFinancialDbContext> _contexts = [];

    public InstitutionalHoldingRepositoryDoubleDownDegeneratePriorTests(ParadeDbFixture fixture) =>
        _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetAsync();

    public Task DisposeAsync()
    {
        foreach (var ctx in _contexts)
            ctx.Dispose();
        return Task.CompletedTask;
    }

    private Equibles.Data.EquiblesFinancialDbContext FreshContext()
    {
        var ctx = _fixture.CreateDbContext();
        _contexts.Add(ctx);
        return ctx;
    }

    private static readonly DateOnly Q4 = new(2025, 12, 31);
    private static readonly DateOnly Q1 = new(2026, 3, 31);

    [Fact]
    public async Task GetDoubleDownPositions_OneSharePriorBase_IsExcludedFromTheReport()
    {
        // "degenerate": 1 share / $70 prior ballooning to 1.9M shares — an effectively
        // new position, not a double down. "genuine": a $100k position grown 50%.
        await using var seed = FreshContext();
        var stock = new CommonStock
        {
            Ticker = "WBS",
            Name = "Webster Financial",
            Cik = "0000801337",
        };
        var degenerate = new InstitutionalHolder { Cik = "degenerate", Name = "Placeholder Prior" };
        var genuine = new InstitutionalHolder { Cik = "genuine", Name = "Conviction Capital" };
        seed.AddRange(stock, degenerate, genuine);
        await seed.SaveChangesAsync();

        seed.AddRange(
            Holding(stock, degenerate, Q4, shares: 1, value: 70, "acc-degenerate-q4"),
            Holding(
                stock,
                degenerate,
                Q1,
                shares: 1_908_982,
                value: 132_521_000,
                "acc-degenerate-q1"
            ),
            Holding(stock, genuine, Q4, shares: 1_000, value: 100_000, "acc-genuine-q4"),
            Holding(stock, genuine, Q1, shares: 1_500, value: 150_000, "acc-genuine-q1")
        );
        await seed.SaveChangesAsync();

        await using var read = FreshContext();
        var sut = new InstitutionalHoldingRepository(read);

        var positions = await sut.GetDoubleDownPositions(Q1, Q4, 50.0).ToListAsync();

        positions
            .Should()
            .ContainSingle("a 1-share prior base is a new position, not a double down");
        positions[0].FilerCik.Should().Be("genuine");
    }

    private static InstitutionalHolding Holding(
        CommonStock stock,
        InstitutionalHolder holder,
        DateOnly reportDate,
        long shares,
        long value,
        string accession
    ) =>
        new()
        {
            CommonStockId = stock.Id,
            InstitutionalHolderId = holder.Id,
            FilingDate = reportDate.AddDays(45),
            ReportDate = reportDate,
            Shares = shares,
            Value = value,
            ShareType = ShareType.Shares,
            InvestmentDiscretion = InvestmentDiscretion.Sole,
            AccessionNumber = accession,
        };
}
