using Equibles.CommonStocks.Data.Models;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;

namespace Equibles.IntegrationTests.Holdings;

/// <summary>
/// Boundary pin for the degenerate-prior-base filter: the contract requires the prior
/// position's value to be AT LEAST MinDoubleDownPreviousValue ($10k), so a prior worth
/// exactly $10,000 is a qualifying base and its conviction increase must stay in the
/// report — an off-by-one to a strict comparison would silently drop it.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class InstitutionalHoldingRepositoryDoubleDownBoundaryValueTests : IAsyncLifetime
{
    private readonly ParadeDbFixture _fixture;
    private readonly List<Equibles.Data.EquiblesFinancialDbContext> _contexts = [];

    public InstitutionalHoldingRepositoryDoubleDownBoundaryValueTests(ParadeDbFixture fixture) =>
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
    public async Task GetDoubleDownPositions_PriorValueExactlyAtMinimum_IsIncludedInTheReport()
    {
        await using var seed = FreshContext();
        var stock = new CommonStock
        {
            Ticker = "WBS",
            Name = "Webster Financial",
            Cik = "0000801337",
        };
        var boundary = new InstitutionalHolder { Cik = "boundary", Name = "Threshold Capital" };
        seed.AddRange(stock, boundary);
        await seed.SaveChangesAsync();

        seed.AddRange(
            Holding(
                stock,
                boundary,
                Q4,
                shares: 1_000,
                value: InstitutionalHoldingRepository.MinDoubleDownPreviousValue,
                "acc-boundary-q4"
            ),
            Holding(stock, boundary, Q1, shares: 1_500, value: 15_000, "acc-boundary-q1")
        );
        await seed.SaveChangesAsync();

        await using var read = FreshContext();
        var sut = new InstitutionalHoldingRepository(read);

        var positions = await sut.GetDoubleDownPositions(Q1, Q4, 50.0).ToListAsync();

        positions
            .Should()
            .ContainSingle("a prior base worth exactly the $10k minimum qualifies (\"at least\")");
        positions[0].FilerCik.Should().Be("boundary");
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
