using System.Reflection;
using Equibles.CommonStocks.Data.Models;
using Equibles.Core.Configuration;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Models;
using Equibles.Holdings.HostedService.Services;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Equibles.IntegrationTests.Holdings;

/// <summary>
/// Contract for the filing-summary orphan cleanup: only filings inside the
/// touched (holder, quarter) pairs may be deleted. The cleanup query loads the
/// holderIds × reportDates cross-product, so a filing of holder A in quarter Q2
/// must survive a sync that touched (A, Q1) and (B, Q2) — deleting it would
/// silently drop a real filing from the feed.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class HoldingsImportServiceSyncFilingSummariesOrphanCleanupTests : IAsyncLifetime
{
    private static readonly DateOnly Q1 = new(2026, 3, 31);
    private static readonly DateOnly Q2 = new(2026, 6, 30);

    private readonly ParadeDbFixture _fixture;

    public HoldingsImportServiceSyncFilingSummariesOrphanCleanupTests(ParadeDbFixture fixture) =>
        _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task SyncFilingSummaries_UntouchedHolderQuarterFiling_SurvivesCrossProductCleanup()
    {
        var holderA = new InstitutionalHolder { Cik = "111", Name = "Alpha Capital" };
        var holderB = new InstitutionalHolder { Cik = "222", Name = "Beta Capital" };
        var stock = new CommonStock { Ticker = "TST", Name = "Test Corp" };
        // Holder A's pre-existing Q2 filing sits outside the touched pairs; its
        // accession has no holdings rows, so a cross-product cleanup would kill it.
        var untouched = new InstitutionalFiling
        {
            AccessionNumber = "OLD-A-Q2",
            InstitutionalHolderId = holderA.Id,
            FilingDate = new DateOnly(2026, 8, 1),
            ReportDate = Q2,
            PositionCount = 1,
            TotalValue = 50,
        };
        await using (var seed = _fixture.CreateDbContext())
        {
            seed.Add(holderA);
            seed.Add(holderB);
            seed.Add(stock);
            seed.Add(untouched);
            seed.Add(MakeHolding(holderA, stock, "ACC-A-Q1", Q1));
            seed.Add(MakeHolding(holderB, stock, "ACC-B-Q2", Q2));
            await seed.SaveChangesAsync();
        }

        var context = new ImportContext
        {
            Submissions = new Dictionary<string, SubmissionRow>
            {
                ["ACC-A-Q1"] = new SubmissionRow
                {
                    AccessionNumber = "ACC-A-Q1",
                    Cik = "111",
                    PeriodOfReport = "2026-03-31",
                },
                ["ACC-B-Q2"] = new SubmissionRow
                {
                    AccessionNumber = "ACC-B-Q2",
                    Cik = "222",
                    PeriodOfReport = "2026-06-30",
                },
            },
            CikToHolderId = new Dictionary<string, Guid>
            {
                ["111"] = holderA.Id,
                ["222"] = holderB.Id,
            },
        };

        await InvokeSyncFilingSummaries(context);

        await using var verify = _fixture.CreateDbContext();
        var survivor = await verify
            .Set<InstitutionalFiling>()
            .SingleOrDefaultAsync(f => f.AccessionNumber == "OLD-A-Q2");
        survivor.Should().NotBeNull("the (A, Q2) pair was not touched by this sync");
    }

    private static InstitutionalHolding MakeHolding(
        InstitutionalHolder holder,
        CommonStock stock,
        string accession,
        DateOnly reportDate
    ) =>
        new InstitutionalHolding
        {
            InstitutionalHolderId = holder.Id,
            CommonStockId = stock.Id,
            AccessionNumber = accession,
            FilingDate = new DateOnly(2026, 8, 15),
            ReportDate = reportDate,
            Value = 100,
            Shares = 10,
            FilingType = FilingType.Form13F,
        };

    private async Task InvokeSyncFilingSummaries(ImportContext context)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => _fixture.CreateDbContext());
        await using var provider = services.BuildServiceProvider();

        var service = new HoldingsImportService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<HoldingsImportService>.Instance,
            Options.Create(new WorkerOptions()),
            stockPriceProvider: null,
            bus: null
        );

        var method = typeof(HoldingsImportService).GetMethod(
            "SyncFilingSummaries",
            BindingFlags.NonPublic | BindingFlags.Instance
        );
        method.Should().NotBeNull();
        await (Task)method.Invoke(service, [context, CancellationToken.None]);
    }
}
