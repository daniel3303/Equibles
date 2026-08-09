using System.Net;
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
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Equibles.UnitTests.GovernmentContracts;

// Contract: USAspending 502s some individual recipient profiles PERMANENTLY (observed
// live: the first epoch-rescan window carried one, and treating its 5xx as systemic
// wedged the whole lane for hours — 22 aborted cycles). An isolated post-retry-ladder
// server error on one recipient is therefore cached as a short-TTL unavailability row and
// the scan continues; only repeated failures in one window (a source outage) still fail
// the window, so an outage can never mass-cache "no parent" answers. Resolutions
// completed before an abort are banked so retries don't refetch them.
public class GovernmentContractsImportServicePoisonedProfileTests
{
    [Fact]
    public async Task Import_OnePoisonedProfile_IsSkippedCachedShortTtl_AndTheWindowCompletes()
    {
        var options = NewDbOptions();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var stockId = SeedCompany(options);
        var scopeFactory = ScopeFactory(options);

        // Two unmatched recipients: one healthy (resolves via parent), one poisoned.
        var client = ClientReturning(
            Award("CONT_AWD_1", "CACI, INC. - FEDERAL", "good-C", today),
            Award("CONT_AWD_2", "BROKEN PROFILE LLC", "poisoned-C", today)
        );
        client
            .GetRecipientProfile("good-C", Arg.Any<CancellationToken>())
            .Returns(
                new UsaSpendingRecipientProfile
                {
                    RecipientId = "good-C",
                    ParentId = "par-P",
                    ParentName = "CACI INTERNATIONAL INC",
                    ParentDuns = "045534641",
                }
            );
        client
            .GetRecipientProfile("poisoned-C", Arg.Any<CancellationToken>())
            .ThrowsAsync(
                new HttpRequestException("502", inner: null, statusCode: HttpStatusCode.BadGateway)
            );

        await NewService(scopeFactory, client).Import(CancellationToken.None);

        using var ctx = NewContext(options);
        ctx.Set<GovernmentContract>()
            .AsNoTracking()
            .Single()
            .CommonStockId.Should()
            .Be(stockId, "the healthy recipient still resolves in the same window");

        var rows = ctx.Set<GovernmentContractRecipientParent>().AsNoTracking().ToList();
        rows.Should().HaveCount(2);
        var poisoned = rows.Single(r => r.RecipientId == "poisoned-C");
        poisoned.ProfileFetchFailed.Should().BeTrue();
        poisoned.ParentNames.Should().BeNull();
        rows.Single(r => r.RecipientId == "good-C").ProfileFetchFailed.Should().BeFalse();

        // The next cycle answers the poisoned recipient from the failure row.
        var secondClient = ClientReturning(
            Award("CONT_AWD_3", "BROKEN PROFILE LLC", "poisoned-C", today)
        );
        await NewService(scopeFactory, secondClient).Import(CancellationToken.None);
        await secondClient
            .DidNotReceive()
            .GetRecipientProfile(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void IsFresh_FailureRows_ReResolveOnTheShortWindow()
    {
        var now = DateTime.UtcNow;
        var reading = new GovernmentContractRecipientParent { ResolvedAt = now.AddDays(-30) };
        var failure = new GovernmentContractRecipientParent
        {
            ResolvedAt = now.AddDays(-30),
            ProfileFetchFailed = true,
        };

        GovernmentContractsImportService.IsFresh(reading, now).Should().BeTrue();
        GovernmentContractsImportService
            .IsFresh(failure, now)
            .Should()
            .BeFalse("an unavailability must retry long before the 180-day reading window");
        GovernmentContractsImportService
            .IsFresh(
                new GovernmentContractRecipientParent
                {
                    ResolvedAt = now.AddDays(-2),
                    ProfileFetchFailed = true,
                },
                now
            )
            .Should()
            .BeTrue();
    }

    [Fact]
    public async Task Import_ManyProfileFailuresInOneWindow_IsAnOutage_FailsTheWindow_ButBanksSuccesses()
    {
        var options = NewDbOptions();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        SeedCompany(options);
        var scopeFactory = ScopeFactory(options);

        // One healthy resolution first, then four distinct server-failing profiles —
        // past the per-window budget, so the window must fail (source outage semantics).
        var awards = new List<UsaSpendingAwardRecord>
        {
            Award("CONT_AWD_OK", "CACI, INC. - FEDERAL", "good-C", today),
        };
        for (var i = 0; i < 4; i++)
            awards.Add(Award($"CONT_AWD_BAD{i}", $"OUTAGE VICTIM {i} LLC", $"bad{i}-C", today));

        var client = ClientReturning(awards.ToArray());
        client
            .GetRecipientProfile("good-C", Arg.Any<CancellationToken>())
            .Returns(
                new UsaSpendingRecipientProfile
                {
                    RecipientId = "good-C",
                    ParentId = "par-P",
                    ParentName = "CACI INTERNATIONAL INC",
                    ParentDuns = "045534641",
                }
            );
        foreach (var i in Enumerable.Range(0, 4))
            client
                .GetRecipientProfile($"bad{i}-C", Arg.Any<CancellationToken>())
                .ThrowsAsync(
                    new HttpRequestException(
                        "502",
                        inner: null,
                        statusCode: HttpStatusCode.BadGateway
                    )
                );

        var act = () => NewService(scopeFactory, client).Import(CancellationToken.None);
        await act.Should().ThrowAsync<HttpRequestException>();

        using var ctx = NewContext(options);
        // No contracts (the window aborted before mapping), but the completed resolutions
        // — including the in-budget failure rows — are banked for the retry.
        ctx.Set<GovernmentContract>().AsNoTracking().Should().BeEmpty();
        var rows = ctx.Set<GovernmentContractRecipientParent>().AsNoTracking().ToList();
        rows.Should().Contain(r => r.RecipientId == "good-C" && !r.ProfileFetchFailed);
        rows.Count(r => r.ProfileFetchFailed).Should().Be(3, "the in-budget failures bank too");
    }

    private static UsaSpendingAwardRecord Award(
        string id,
        string recipientName,
        string recipientId,
        DateOnly actionDate
    ) =>
        new()
        {
            GeneratedInternalId = id,
            AwardId = id,
            RecipientName = recipientName,
            RecipientId = recipientId,
            Amount = 5_000_000m,
            ContractAwardType = "DEFINITIVE CONTRACT",
            BaseObligationDate = actionDate.ToString("yyyy-MM-dd"),
        };

    private static IUsaSpendingClient ClientReturning(params UsaSpendingAwardRecord[] awards)
    {
        var client = Substitute.For<IUsaSpendingClient>();
        client
            .GetContractAwards(
                Arg.Any<DateOnly>(),
                Arg.Any<DateOnly>(),
                Arg.Any<decimal>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.FromResult(awards.ToList()));
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
            Options.Create(
                new GovernmentContractsScraperOptions
                {
                    WindowDays = 1,
                    RescanLookbackDays = 1,
                    MinimumAwardAmount = 1_000_000m,
                }
            ),
            Options.Create(new WorkerOptions { MinSyncDate = today.ToDateTime(TimeOnly.MinValue) }),
            new ErrorReporter(scopeFactory, NullLogger<ErrorReporter>.Instance)
        );
    }

    private static Guid SeedCompany(DbContextOptions<EquiblesFinancialDbContext> options)
    {
        using var seed = NewContext(options);
        var stock = new CommonStock
        {
            Ticker = "CACI",
            Name = "Caci International Inc /De/",
            Cik = "1",
        };
        seed.Add(stock);
        seed.SaveChanges();
        return stock.Id;
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
