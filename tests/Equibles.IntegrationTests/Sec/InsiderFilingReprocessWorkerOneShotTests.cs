using System.Reflection;
using Equibles.CommonStocks.Data.Models;
using Equibles.CorporateActions.Repositories;
using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.InsiderTrading.BusinessLogic;
using Equibles.InsiderTrading.Data.Models;
using Equibles.InsiderTrading.Repositories;
using Equibles.Integrations.Sec.Contracts;
using Equibles.IntegrationTests.Helpers;
using Equibles.IntegrationTests.InsiderTrading;
using Equibles.Sec.HostedService;
using Equibles.Sec.Repositories;
using Equibles.Yahoo.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Equibles.IntegrationTests.Sec;

/// <summary>
/// Pins the worker's one-shot gate: the sharesless reopen re-matches every row the validator
/// re-flags, so it runs until it drains below the cycle bound and the marker keeps it off later cycles.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class InsiderFilingReprocessWorkerOneShotTests : ParadeDbMcpTestBase
{
    private const string Marker = "InsiderTrading.ShareslessVerdictReopen";

    public InsiderFilingReprocessWorkerOneShotTests(ParadeDbFixture fixture)
        : base(fixture) { }

    private ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();
        services.AddScoped<EquiblesFinancialDbContext>(_ => Fixture.CreateDbContext());
        services.AddScoped<InsiderTransactionRepository>();
        services.AddScoped<InsiderFilingRepository>();
        services.AddScoped<EquityDailyStockPriceRepository>();
        services.AddScoped<StockSplitRepository>();
        services.AddScoped<BackfillStateRepository>();
        services.AddScoped<InsiderMisrepairedPriceSweep>();
        services.AddScoped<InsiderFilingReprocessManager>();
        services.AddScoped<InsiderTransactionPriceBackfillManager>();
        services.AddSingleton<InsiderTransactionPriceValidator>();
        services.AddSingleton(Substitute.For<ISecEdgarClient>());
        services.AddSingleton(InsiderReprocessTestSupport.NewFileManager());
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        return services.BuildServiceProvider();
    }

    private static Task RunCycle(InsiderFilingReprocessWorker worker)
    {
        var doWork = typeof(InsiderFilingReprocessWorker).GetMethod(
            "DoWork",
            BindingFlags.NonPublic | BindingFlags.Instance
        );
        return (Task)doWork!.Invoke(worker, [CancellationToken.None]);
    }

    [Fact]
    public async Task DoWork_ShareslessReopen_RunsOnceThenTheMarkerHolds()
    {
        // No issuer bar exists, so the backfill leaves a reopened row pending rather than re-judging it.
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Id: Guid.NewGuid(),
            Ticker: "AMZN",
            Name: "Amazon.com, Inc.",
            Cik: "0001018724"
        );
        var owner = new InsiderOwner
        {
            Id = Guid.NewGuid(),
            OwnerCik = "0001420149",
            Name = "Insider Owner",
            City = "Seattle",
            StateOrCountry = "WA",
        };
        var row = new InsiderTransaction
        {
            Id = Guid.NewGuid(),
            EquityIssuerId = stock.Id,
            InsiderOwnerId = owner.Id,
            AccessionNumber = Guid.NewGuid().ToString()[..20],
            TransactionOrder = 0,
            FilingDate = new DateOnly(2021, 2, 18),
            TransactionDate = new DateOnly(2021, 2, 16),
            TransactionCode = TransactionCode.Sale,
            Shares = 0,
            PricePerShare = 3_300.24m,
            ReportedPricePerShare = 3_300.24m,
            IsPriceValid = false,
            AcquiredDisposed = AcquiredDisposed.Disposed,
            OwnershipNature = OwnershipNature.Direct,
            SecurityTitle = "Common Stock",
            SecurityKind = InsiderSecurityKind.NonDerivative,
            ParserVersion = InsiderTransaction.CurrentParserVersion,
        };
        DbContext.Add(stock);
        DbContext.Add(owner);
        DbContext.Add(row);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await using var provider = BuildServices();
        var worker = new InsiderFilingReprocessWorker(
            Substitute.For<ILogger<InsiderFilingReprocessWorker>>(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            new ErrorReporter(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            )
        );

        await RunCycle(worker);

        await using (var verify = Fixture.CreateDbContext())
        {
            var reopened = await verify.Set<InsiderTransaction>().SingleAsync(t => t.Id == row.Id);
            reopened.IsPriceValid.Should().BeNull();
            (await new BackfillStateRepository(verify).GetByName(Marker)).Should().NotBeNull();

            // The validator re-flags the row later; the reopen predicate matches it again.
            reopened.IsPriceValid = false;
            await verify.SaveChangesAsync();
        }

        await RunCycle(worker);

        await using var final = Fixture.CreateDbContext();
        (await final.Set<InsiderTransaction>().SingleAsync(t => t.Id == row.Id))
            .IsPriceValid.Should()
            .BeFalse("the marker keeps the reopen off every later cycle");
    }
}
