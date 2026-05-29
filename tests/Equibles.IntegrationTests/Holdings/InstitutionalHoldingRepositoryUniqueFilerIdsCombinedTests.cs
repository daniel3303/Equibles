using Equibles.CommonStocks.Data;
using Equibles.Data;
using Equibles.Holdings.Data;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;

namespace Equibles.IntegrationTests.Holdings;

public class InstitutionalHoldingRepositoryUniqueFilerIdsCombinedTests : IDisposable
{
    private readonly EquiblesFinancialDbContext _dbContext;
    private readonly InstitutionalHoldingRepository _repository;

    public InstitutionalHoldingRepositoryUniqueFilerIdsCombinedTests()
    {
        _dbContext = TestDbContextFactory.Create(
            new CommonStocksModuleConfiguration(),
            new HoldingsModuleConfiguration()
        );
        _repository = new InstitutionalHoldingRepository(_dbContext);
    }

    public void Dispose() => _dbContext.Dispose();

    // The distinct set of filers in the combined view = current filers PLUS
    // previous-quarter non-filers carried forward. A current filer reporting
    // multiple stocks must be counted ONCE (Distinct); a non-filer who held last
    // quarter is still in the universe. This is the "% of 13F universe" denominator.
    [Fact]
    public async Task GetUniqueFilerIdsCombined_DedupesMultiStockFiler_AndIncludesCarriedNonFiler()
    {
        var stockX = Guid.NewGuid();
        var stockY = Guid.NewGuid();
        var filer = new InstitutionalHolder
        {
            Id = Guid.NewGuid(),
            Cik = "0000000001",
            Name = "Filed Two Stocks",
        };
        var nonFiler = new InstitutionalHolder
        {
            Id = Guid.NewGuid(),
            Cik = "0000000002",
            Name = "Carried Forward",
        };
        var previous = new DateOnly(2024, 3, 31);
        var current = new DateOnly(2024, 6, 30);

        _dbContext.Set<InstitutionalHolder>().AddRange(filer, nonFiler);
        _dbContext
            .Set<InstitutionalHolding>()
            .AddRange(
                // Filer reports two stocks this quarter — must collapse to one id.
                Holding(stockX, filer.Id, current, "F-X-Q2"),
                Holding(stockY, filer.Id, current, "F-Y-Q2"),
                // Non-filer held last quarter only — carried into the combined view.
                Holding(stockX, nonFiler.Id, previous, "N-X-Q1")
            );
        await _dbContext.SaveChangesAsync(CancellationToken.None);

        var ids = await _repository
            .GetUniqueFilerIdsCombined(current, previous)
            .ToListAsync(CancellationToken.None);

        ids.Should().BeEquivalentTo([filer.Id, nonFiler.Id]);
    }

    private static InstitutionalHolding Holding(
        Guid stockId,
        Guid holderId,
        DateOnly reportDate,
        string accession
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            CommonStockId = stockId,
            InstitutionalHolderId = holderId,
            ReportDate = reportDate,
            FilingDate = reportDate,
            Shares = 100,
            Value = 1000,
            AccessionNumber = accession,
        };
}
