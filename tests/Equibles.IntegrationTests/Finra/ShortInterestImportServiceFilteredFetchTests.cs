using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.Configuration;
using Equibles.Errors.BusinessLogic;
using Equibles.Finra.Data.Models;
using Equibles.Finra.HostedService.Services;
using Equibles.Finra.Repositories;
using Equibles.Integrations.Finra.Contracts;
using Equibles.Integrations.Finra.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Equibles.IntegrationTests.Finra;

/// <summary>
/// <see cref="ShortInterestImportServicePipelineTests"/> drives the bulk-fetch
/// branch (all tracked stocks missing for the date). This pins the
/// filtered-fetch branch: when only some tracked stocks are missing, the
/// service fetches just those symbols via the symbol-scoped GetShortInterest
/// overload.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class ShortInterestImportServiceFilteredFetchTests : ParadeDbMcpTestBase
{
    public ShortInterestImportServiceFilteredFetchTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task Import_OnlySomeStocksMissing_UsesSymbolScopedFetch()
    {
        var settlementDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);

        var have = new CommonStock
        {
            Cik = "0000000111",
            Ticker = "HAVE",
            Name = "Already Has Data Inc.",
        };
        var missing = new CommonStock
        {
            Cik = "0000000222",
            Ticker = "MISS",
            Name = "Needs Data Inc.",
        };
        DbContext.AddRange(have, missing);
        // HAVE already has short interest for the date → not missing, so the
        // missing set ({MISS}) is a strict subset of tracked → filtered fetch.
        DbContext.Add(
            new ShortInterest
            {
                CommonStockId = have.Id,
                SettlementDate = settlementDate,
                CurrentShortPosition = 1,
            }
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var finraClient = Substitute.For<IFinraClient>();
        // A seeded row makes maxKnownDate non-default, so discovery uses the
        // "after" overload; the date is already known so it adds nothing new.
        finraClient
            .GetShortInterestSettlementDatesAfter(Arg.Any<DateOnly>())
            .Returns(new List<DateOnly>());
        finraClient
            .GetShortInterest(settlementDate, Arg.Any<IReadOnlyList<string>>())
            .Returns(
                new List<ShortInterestRecord>
                {
                    new()
                    {
                        Symbol = "MISS",
                        CurrentShortPosition = 750_000,
                        PreviousShortPosition = 600_000,
                        ChangeInShortPosition = 150_000,
                        AverageDailyVolume = 2_000_000,
                        DaysToCover = 0.4m,
                    },
                }
            );

        var scopeFactory = ServiceScopeSubstitute.Create(
            (typeof(CommonStockRepository), new CommonStockRepository(DbContext)),
            (typeof(ShortInterestRepository), new ShortInterestRepository(DbContext))
        );
        var sut = new ShortInterestImportService(
            scopeFactory,
            Substitute.For<ILogger<ShortInterestImportService>>(),
            finraClient,
            new TickerMapService(scopeFactory),
            new ErrorReporter(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            ),
            Options.Create(
                new WorkerOptions { TickersToSync = [], MinSyncDate = DateTime.UtcNow.AddDays(-30) }
            )
        );

        await sut.Import(CancellationToken.None);

        await finraClient
            .Received()
            .GetShortInterest(settlementDate, Arg.Any<IReadOnlyList<string>>());
        await finraClient.DidNotReceive().GetShortInterest(Arg.Any<DateOnly>());

        await using var verify = Fixture.CreateDbContext();
        var missRow = await verify
            .Set<ShortInterest>()
            .AsNoTracking()
            .SingleOrDefaultAsync(s =>
                s.CommonStockId == missing.Id && s.SettlementDate == settlementDate
            );
        missRow.Should().NotBeNull("the symbol-scoped fetch must persist the missing stock");
        missRow!.CurrentShortPosition.Should().Be(750_000);
    }
}
