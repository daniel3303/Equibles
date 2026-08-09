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

// Contract: an award whose recipient name matches no public company gets ONE second chance
// through its SAM-registered parent — resolved via the recipient-profile endpoint, matched
// through the same exact normalised lookup, and cached (including the "no usable parent"
// answer) so a recipient costs at most one profile fetch per staleness window. A profile
// fetch that fails transport-level fails the WINDOW, never silently skips the recipient.
public class GovernmentContractsImportServiceParentFallbackTests
{
    private const string SubsidiaryName = "CACI, INC. - FEDERAL";
    private const string SubsidiaryRecipientId = "abc123-C";

    [Fact]
    public async Task Import_UnmatchedSubsidiary_ResolvesThroughItsParentAndCachesTheAnswer()
    {
        var options = NewDbOptions();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var stockId = SeedCompany(options);
        var scopeFactory = ScopeFactory(options);

        var client = ClientReturning(AwardFor(today));
        client
            .GetRecipientProfile(SubsidiaryRecipientId, Arg.Any<CancellationToken>())
            .Returns(
                new UsaSpendingRecipientProfile
                {
                    RecipientId = SubsidiaryRecipientId,
                    ParentId = "def456-P",
                    ParentName = "CACI INTERNATIONAL INC",
                    ParentDuns = "045534641",
                }
            );

        await NewService(scopeFactory, client).Import(CancellationToken.None);

        using var ctx = NewContext(options);
        var contract = ctx.Set<GovernmentContract>().AsNoTracking().Single();
        contract.CommonStockId.Should().Be(stockId, "the award resolves through its parent");
        contract.RecipientName.Should().Be(SubsidiaryName);

        var cached = ctx.Set<GovernmentContractRecipientParent>().AsNoTracking().Single();
        cached.RecipientId.Should().Be(SubsidiaryRecipientId);
        cached.ParentRecipientId.Should().Be("def456-P");
        cached.ParentNames.Should().Be("CACI INTERNATIONAL INC");
    }

    [Fact]
    public async Task Import_FreshCachedParent_ResolvesWithoutRefetchingTheProfile()
    {
        var options = NewDbOptions();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var stockId = SeedCompany(options);
        using (var seed = NewContext(options))
        {
            seed.Add(
                new GovernmentContractRecipientParent
                {
                    RecipientId = SubsidiaryRecipientId,
                    RecipientName = SubsidiaryName,
                    ParentRecipientId = "def456-P",
                    ParentNames = "CACI INTERNATIONAL INC",
                    ResolvedAt = DateTime.UtcNow.AddDays(-1),
                }
            );
            seed.SaveChanges();
        }
        var scopeFactory = ScopeFactory(options);

        var client = ClientReturning(AwardFor(today));

        await NewService(scopeFactory, client).Import(CancellationToken.None);

        await client
            .DidNotReceive()
            .GetRecipientProfile(Arg.Any<string>(), Arg.Any<CancellationToken>());
        using var ctx = NewContext(options);
        ctx.Set<GovernmentContract>().AsNoTracking().Single().CommonStockId.Should().Be(stockId);
    }

    [Fact]
    public async Task Import_UnknownRecipient_CachesTheParentlessAnswerOnce()
    {
        var options = NewDbOptions();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        SeedCompany(options);
        var scopeFactory = ScopeFactory(options);

        // The client answers a profile 404 as null — an answer, not a fault.
        var client = ClientReturning(AwardFor(today));
        client
            .GetRecipientProfile(SubsidiaryRecipientId, Arg.Any<CancellationToken>())
            .Returns((UsaSpendingRecipientProfile)null);

        await NewService(scopeFactory, client).Import(CancellationToken.None);
        await NewService(scopeFactory, client).Import(CancellationToken.None);

        // One fetch total across both cycles: the parentless answer was cached.
        await client
            .Received(1)
            .GetRecipientProfile(SubsidiaryRecipientId, Arg.Any<CancellationToken>());
        using var ctx = NewContext(options);
        ctx.Set<GovernmentContract>().AsNoTracking().Should().BeEmpty();
        var cached = ctx.Set<GovernmentContractRecipientParent>().AsNoTracking().Single();
        cached.ParentNames.Should().BeNull();
        cached.ParentRecipientId.Should().BeNull();
    }

    [Fact]
    public async Task Import_ProfileFetchFailsTransportLevel_FailsTheWindowNotSilently()
    {
        var options = NewDbOptions();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        SeedCompany(options);
        var scopeFactory = ScopeFactory(options);

        var client = ClientReturning(AwardFor(today));
        client
            .GetRecipientProfile(SubsidiaryRecipientId, Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("USAspending bad spell"));

        var act = () => NewService(scopeFactory, client).Import(CancellationToken.None);

        // Non-422 is systemic: the cycle aborts so the checkpoint machinery re-covers the
        // window instead of the recipient's awards being silently dropped.
        await act.Should().ThrowAsync<HttpRequestException>();
        using var ctx = NewContext(options);
        ctx.Set<GovernmentContract>().AsNoTracking().Should().BeEmpty();
        ctx.Set<GovernmentContractRecipientParent>().AsNoTracking().Should().BeEmpty();
    }

    private static UsaSpendingAwardRecord AwardFor(DateOnly actionDate) =>
        new()
        {
            GeneratedInternalId = "CONT_AWD_SUB1",
            AwardId = "FA0001",
            RecipientName = SubsidiaryName,
            RecipientId = SubsidiaryRecipientId,
            Amount = 5_000_000m,
            AwardingAgency = "Department of Defense",
            ContractAwardType = "DEFINITIVE CONTRACT",
            BaseObligationDate = actionDate.ToString("yyyy-MM-dd"),
            StartDate = actionDate.ToString("yyyy-MM-dd"),
            EndDate = actionDate.AddYears(2).ToString("yyyy-MM-dd"),
            LastModifiedDate = actionDate.ToString("yyyy-MM-dd"),
            Description = "SERVICES",
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
            // The EDGAR-shaped stored name; the SAM parent name matches it only through the
            // normaliser's slash-marker stripping.
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
