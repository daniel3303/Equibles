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
/// Contract for the filing-summary sync: <see cref="InstitutionalFiling"/> holds one
/// row per accession, so when an accession's holdings rows disagree on grouping
/// metadata (e.g. a mixed amendment flag), the sync must still produce exactly one
/// summary row instead of handing the upsert two rows with the same conflict key —
/// PostgreSQL rejects that with 21000 ("ON CONFLICT DO UPDATE command cannot affect
/// row a second time") and the whole data set import aborts.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class HoldingsImportServiceSyncFilingSummariesDuplicateAccessionTests : IAsyncLifetime
{
    private const string Accession = "0001234567-26-000123";
    private const string Cik = "1234567";
    private static readonly DateOnly ReportDate = new(2026, 3, 31);

    private readonly ParadeDbFixture _fixture;

    public HoldingsImportServiceSyncFilingSummariesDuplicateAccessionTests(
        ParadeDbFixture fixture
    ) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task SyncFilingSummaries_AccessionWithMixedAmendmentFlag_UpsertsSingleSummaryRow()
    {
        var holder = new InstitutionalHolder { Cik = Cik, Name = "Test Capital" };
        var stock = new CommonStock { Ticker = "TST", Name = "Test Corp" };
        await using (var seed = _fixture.CreateDbContext())
        {
            seed.Add(holder);
            seed.Add(stock);
            // Distinct ShareType keeps the rows clear of the holdings unique index;
            // the mixed amendment flag is what splits the summary GROUP BY.
            seed.Add(MakeHolding(holder, stock, ShareType.Shares, isAmendment: false, value: 100));
            seed.Add(
                MakeHolding(holder, stock, ShareType.Principal, isAmendment: true, value: 200)
            );
            await seed.SaveChangesAsync();
        }

        var context = new ImportContext
        {
            Submissions = new Dictionary<string, SubmissionRow>
            {
                [Accession] = new SubmissionRow
                {
                    AccessionNumber = Accession,
                    Cik = Cik,
                    PeriodOfReport = "2026-03-31",
                },
            },
            CikToHolderId = new Dictionary<string, Guid> { [Cik] = holder.Id },
        };

        var act = async () => await InvokeSyncFilingSummaries(context);

        await act.Should().NotThrowAsync();
        await using var verify = _fixture.CreateDbContext();
        var summaries = await verify
            .Set<InstitutionalFiling>()
            .Where(f => f.AccessionNumber == Accession)
            .ToListAsync();
        summaries.Should().ContainSingle();
    }

    private static InstitutionalHolding MakeHolding(
        InstitutionalHolder holder,
        CommonStock stock,
        ShareType shareType,
        bool isAmendment,
        long value
    ) =>
        new InstitutionalHolding
        {
            InstitutionalHolderId = holder.Id,
            CommonStockId = stock.Id,
            AccessionNumber = Accession,
            FilingDate = new DateOnly(2026, 5, 15),
            ReportDate = ReportDate,
            IsAmendment = isAmendment,
            Value = value,
            Shares = 10,
            ShareType = shareType,
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
