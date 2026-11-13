using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.Configuration;
using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data;
using Equibles.Errors.Repositories;
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

/// <summary>
/// A modification brings a stored award back into a window with its current amount and outlays;
/// the stored row must take them, or totals and rankings keep the first-seen figures forever.
/// </summary>
public class GovernmentContractsImportServiceRefreshTests
{
    [Fact]
    public async Task Import_AStoredAwardSeenAgain_TakesItsCurrentAmountAndOutlays()
    {
        var options = NewDbOptions();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        using (var seed = NewContext(options))
        {
            var issuer = Equibles.TestSupport.EquityIssuerSeed.Create(
                Ticker: "LMT",
                Name: "Lockheed Martin Corporation",
                Cik: "1"
            );
            seed.Add(issuer);
            seed.Add(
                new GovernmentContract
                {
                    EquityIssuerId = issuer.Id,
                    AwardUniqueKey = "CONT_AWD_1",
                    AwardId = "PIID1",
                    RecipientName = "LOCKHEED MARTIN CORPORATION",
                    Amount = 5_000_000m,
                    ActionDate = today.AddDays(-400),
                    LastModifiedDate = today.AddDays(-400),
                }
            );
            await seed.SaveChangesAsync();
        }

        var client = Substitute.For<IUsaSpendingClient>();
        client
            .GetContractAwards(
                Arg.Any<DateOnly>(),
                Arg.Any<DateOnly>(),
                Arg.Any<decimal>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Task.FromResult(
                    new List<UsaSpendingAwardRecord>
                    {
                        new()
                        {
                            GeneratedInternalId = "CONT_AWD_1",
                            AwardId = "PIID1",
                            RecipientName = "LOCKHEED MARTIN CORPORATION",
                            Amount = 50_000_000m,
                            TotalOutlays = 20_000_000m,
                            BaseObligationDate = today.AddDays(-400).ToString("yyyy-MM-dd"),
                            LastModifiedDate = today.AddDays(-2).ToString("yyyy-MM-dd"),
                        },
                    }
                )
            );
        var scopeFactory = ScopeFactory(options);
        var service = NewService(scopeFactory, client, DateTime.UtcNow.Date.AddDays(-3));

        await service.Import(CancellationToken.None);

        using var read = NewContext(options);
        var stored = await read.Set<GovernmentContract>().SingleAsync();
        stored.Amount.Should().Be(50_000_000m);
        stored.TotalOutlays.Should().Be(20_000_000m);
        stored.LastModifiedDate.Should().Be(today.AddDays(-2));
    }

    private static GovernmentContractsImportService NewService(
        IServiceScopeFactory scopeFactory,
        IUsaSpendingClient client,
        DateTime minSyncDate
    ) =>
        new(
            scopeFactory,
            NullLogger<GovernmentContractsImportService>.Instance,
            client,
            new RecipientResolver(scopeFactory),
            Options.Create(
                new GovernmentContractsScraperOptions
                {
                    WindowDays = 1,
                    MinimumAwardAmount = 1_000_000m,
                }
            ),
            Options.Create(new WorkerOptions { MinSyncDate = minSyncDate }),
            new ErrorReporter(scopeFactory, NullLogger<ErrorReporter>.Instance)
        );

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
                new ErrorsModuleConfiguration(),
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
        services.AddScoped<EquityIssuerRepository>();
        services.AddScoped<ErrorRepository>();
        services.AddScoped<ErrorManager>();
        services.AddScoped<GovernmentContractRepository>();
        services.AddScoped<GovernmentContractsScanStateRepository>();
        services.AddScoped<GovernmentContractRecipientParentRepository>();
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }
}
