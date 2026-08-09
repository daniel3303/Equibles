using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.Configuration;
using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.GovernmentContracts.Data;
using Equibles.GovernmentContracts.Data.Models;
using Equibles.GovernmentContracts.HostedService.Configuration;
using Equibles.GovernmentContracts.HostedService.Services;
using Equibles.GovernmentContracts.Repositories;
using Equibles.Integrations.GovernmentContracts.Contracts;
using Equibles.Integrations.GovernmentContracts.Models;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Equibles.UnitTests.GovernmentContracts;

// Contract: unmatched awards are dropped, not stored, so a recipient-matching improvement
// only reaches history by re-walking it. A stored checkpoint stamped with an older
// MatchingVersion pulls the cursor back to the 2007-10-01 epoch exactly ONCE (cursor and
// stamp move in one save; the re-walk deduplicates by AwardUniqueKey); a checkpoint born
// under the current version never resets.
public class GovernmentContractsImportServiceMatchingVersionResetTests
{
    private const string ScanStateName = "award-scan";
    private static readonly DateOnly Epoch = new(2007, 10, 1);

    [Fact]
    public async Task Import_CheckpointStampedWithOlderMatchingVersion_RescansFromTheEpochOnce()
    {
        var options = NewDbOptions();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        SeedCompany(options);
        using (var seed = NewContext(options))
        {
            // A caught-up pre-upgrade checkpoint: rows written before the column existed
            // read as version 0.
            seed.Add(
                new GovernmentContractsScanState
                {
                    Name = ScanStateName,
                    LastCompletedWindowEnd = today,
                    UpdatedAt = DateTime.UtcNow,
                    MatchingVersion = 0,
                }
            );
            seed.SaveChanges();
        }
        var scopeFactory = ScopeFactory(options);

        var client = EmptyClient();
        await NewService(scopeFactory, client).Import(CancellationToken.None);

        // The rescan starts at the epoch, not the trailing lookback.
        await client
            .Received()
            .GetContractAwards(
                Epoch,
                Arg.Any<DateOnly>(),
                Arg.Any<decimal>(),
                Arg.Any<CancellationToken>()
            );
        ReadState(options)
            .MatchingVersion.Should()
            .Be(GovernmentContractsImportService.RecipientMatchingVersion);

        // Once only: the next cycle resumes from the re-advanced checkpoint.
        var secondClient = EmptyClient();
        await NewService(scopeFactory, secondClient).Import(CancellationToken.None);
        await secondClient
            .DidNotReceive()
            .GetContractAwards(
                Epoch,
                Arg.Any<DateOnly>(),
                Arg.Any<decimal>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Import_FreshInstall_ChecksPointBornCurrent_NeverTriggersTheEpochRescan()
    {
        var options = NewDbOptions();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        SeedCompany(options);
        var scopeFactory = ScopeFactory(options);

        var client = EmptyClient();
        await NewService(scopeFactory, client).Import(CancellationToken.None);

        // The first cycle creates the row stamped current…
        ReadState(options)
            .MatchingVersion.Should()
            .Be(GovernmentContractsImportService.RecipientMatchingVersion);

        // …so the second cycle scans its configured range, never the 2007 epoch.
        var secondClient = EmptyClient();
        await NewService(scopeFactory, secondClient).Import(CancellationToken.None);
        await secondClient
            .DidNotReceive()
            .GetContractAwards(
                Epoch,
                Arg.Any<DateOnly>(),
                Arg.Any<decimal>(),
                Arg.Any<CancellationToken>()
            );
        await secondClient
            .Received()
            .GetContractAwards(
                today,
                Arg.Any<DateOnly>(),
                Arg.Any<decimal>(),
                Arg.Any<CancellationToken>()
            );
    }

    private static IUsaSpendingClient EmptyClient()
    {
        var client = Substitute.For<IUsaSpendingClient>();
        client
            .GetContractAwards(
                Arg.Any<DateOnly>(),
                Arg.Any<DateOnly>(),
                Arg.Any<decimal>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.FromResult(new List<UsaSpendingAwardRecord>()));
        return client;
    }

    private static GovernmentContractsImportService NewService(
        IServiceScopeFactory scopeFactory,
        IUsaSpendingClient client
    )
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return new(
            scopeFactory,
            NullLogger<GovernmentContractsImportService>.Instance,
            client,
            new RecipientResolver(scopeFactory),
            // One huge window keeps the epoch rescan a single client call instead of ~6,900.
            Options.Create(
                new GovernmentContractsScraperOptions
                {
                    WindowDays = 100_000,
                    RescanLookbackDays = 1,
                    MinimumAwardAmount = 1_000_000m,
                }
            ),
            Options.Create(new WorkerOptions { MinSyncDate = today.ToDateTime(TimeOnly.MinValue) }),
            new ErrorReporter(scopeFactory, NullLogger<ErrorReporter>.Instance)
        );
    }

    private static void SeedCompany(DbContextOptions<EquiblesFinancialDbContext> options)
    {
        using var seed = NewContext(options);
        seed.Add(
            new CommonStock
            {
                Ticker = "LMT",
                Name = "Lockheed Martin Corporation",
                Cik = "1",
            }
        );
        seed.SaveChanges();
    }

    private static GovernmentContractsScanState ReadState(
        DbContextOptions<EquiblesFinancialDbContext> options
    )
    {
        using var ctx = NewContext(options);
        return ctx.Set<GovernmentContractsScanState>()
            .AsNoTracking()
            .Single(s => s.Name == ScanStateName);
    }

    private static DbContextOptions<EquiblesFinancialDbContext> NewDbOptions() =>
        new DbContextOptionsBuilder<EquiblesFinancialDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString(), new InMemoryDatabaseRoot())
            .EnableServiceProviderCaching(false)
            .Options;

    private static EquiblesFinancialDbContext NewContext(
        DbContextOptions<EquiblesFinancialDbContext> options
    )
    {
        var ctx = new EquiblesFinancialDbContext(
            options,
            new IModuleConfiguration[]
            {
                new CommonStocksModuleConfiguration(),
                new GovernmentContractsModuleConfiguration(),
            }
        );
        ctx.Database.EnsureCreated();
        return ctx;
    }

    private static IServiceScopeFactory ScopeFactory(
        DbContextOptions<EquiblesFinancialDbContext> options
    )
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => NewContext(options));
        services.AddScoped<CommonStockRepository>();
        services.AddScoped<GovernmentContractRepository>();
        services.AddScoped<GovernmentContractsScanStateRepository>();
        services.AddScoped<GovernmentContractRecipientParentRepository>();
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }
}
