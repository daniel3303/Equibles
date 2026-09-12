using Equibles.CommonStocks.Data.Models;
using Equibles.Holdings.BusinessLogic;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;

namespace Equibles.IntegrationTests.Holdings;

[Collection(ParadeDbCollection.Name)]
public class HoldingsComparisonCoveragePostgresTests(ParadeDbFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Theory]
    [InlineData(4)]
    [InlineData(20)]
    public async Task History_BatchesEvidenceRegardlessOfQuarterCount(int quarterCount)
    {
        var stock = new CommonStock { Ticker = "COVER", Name = "Coverage subject" };
        var other = new CommonStock { Ticker = "OTHER", Name = "Other issuer" };
        var stable = new InstitutionalHolder { Cik = "1", Name = "Stable filer" };
        var rotating = new InstitutionalHolder { Cik = "2", Name = "Rotating filer" };
        var dates = Enumerable
            .Range(0, quarterCount)
            .Select(i => new DateOnly(2020, 1, 1).AddMonths((i + 1) * 3).AddDays(-1))
            .ToArray();
        await using (var seed = fixture.CreateDbContext())
        {
            seed.AddRange(stock, other, stable, rotating);
            for (var i = 0; i < dates.Length; i++)
            {
                seed.Add(Holding(stock, stable, dates[i]));
                seed.Add(Holding(i % 2 == 0 ? stock : other, rotating, dates[i]));
            }
            await seed.SaveChangesAsync();
        }
        var counter = new HoldingsReadCounter();
        await using var db = fixture.CreateDbContext(options => options.AddInterceptors(counter));
        var result = await HoldingsComparisonCoverage.History(
            new InstitutionalHoldingRepository(db),
            stock,
            dates
        );
        result.Should().HaveCount(quarterCount - 1);
        result.Values.Should().OnlyContain(status => status.ComparisonAvailable);
        counter
            .Reads.Should()
            .Be(2, "position presence and missing-side evidence each use one batched read");
    }

    private static InstitutionalHolding Holding(
        CommonStock stock,
        InstitutionalHolder holder,
        DateOnly date
    ) =>
        new()
        {
            EquityIssuerId = stock.Id,
            InstitutionalHolderId = holder.Id,
            ReportDate = date,
            FilingDate = date.AddDays(40),
            FilingType = FilingType.Form13F,
            Shares = 100,
            Value = 10000,
            AccessionNumber = Guid.NewGuid().ToString("N"),
            ShareType = ShareType.Shares,
        };
}
