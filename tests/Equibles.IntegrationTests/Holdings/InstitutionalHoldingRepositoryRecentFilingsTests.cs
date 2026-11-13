using Equibles.CommonStocks.Data;
using Equibles.CorporateActions.Data;
using Equibles.Data;
using Equibles.Holdings.Data;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;

namespace Equibles.IntegrationTests.Holdings;

public class InstitutionalHoldingRepositoryRecentFilingsTests : IDisposable
{
    private readonly EquiblesFinancialDbContext _dbContext = TestDbContextFactory.Create(
        new CommonStocksModuleConfiguration(),
        new HoldingsModuleConfiguration(),
        new CorporateActionsModuleConfiguration()
    );

    public void Dispose() => _dbContext.Dispose();

    // The rollup also stores Schedule 13D/G event rows; the recent-filings feeds list 13F books only.
    [Fact]
    public async Task GetRecentFilings_FilerWithA13GEvent_ListsOnlyThe13FFiling()
    {
        var holder = new InstitutionalHolder { Cik = "0001067983", Name = "Example Capital" };
        _dbContext.Add(holder);
        _dbContext.AddRange(
            Filing(holder, "0001067983-26-000001", FilingType.Form13F),
            Filing(holder, "0001067983-26-000002", FilingType.Schedule13G)
        );
        await _dbContext.SaveChangesAsync(CancellationToken.None);

        var filings = await new InstitutionalHoldingRepository(_dbContext)
            .GetRecentFilings()
            .ToListAsync(CancellationToken.None);

        filings.Select(f => f.AccessionNumber).Should().Equal("0001067983-26-000001");
    }

    private static InstitutionalFiling Filing(
        InstitutionalHolder holder,
        string accession,
        FilingType type
    ) =>
        new()
        {
            InstitutionalHolderId = holder.Id,
            AccessionNumber = accession,
            FilingDate = new DateOnly(2026, 11, 2),
            ReportDate = new DateOnly(2026, 9, 30),
            FilingType = type,
            PositionCount = 1,
            TotalValue = 1_000,
        };
}
