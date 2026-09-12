using System.Data.Common;
using Equibles.CommonStocks.Data.Models;
using Equibles.Core.Contracts;
using Equibles.Data;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Services;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Equibles.IntegrationTests.Holdings;

[Collection(ParadeDbCollection.Name)]
public class PrincipalValueRepairTransactionTests(ParadeDbFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task FailedRollupRollsBackPositionAndCanRetry()
    {
        var date = new DateOnly(2026, 6, 30);
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "PRN",
            Name: "Principal issuer"
        );
        var holder = new InstitutionalHolder { Cik = "0001900923", Name = "Principal filer" };
        var holding = new InstitutionalHolding
        {
            EquityIssuerId = stock.Id,
            InstitutionalHolder = holder,
            ReportDate = date,
            FilingDate = date.AddDays(40),
            AccessionNumber = "0001214659-26-010383",
            ShareType = ShareType.Principal,
            Shares = 900_000,
            Value = 45_000_000,
            FiledValue = 45_000,
            ManagerEntries =
            [
                new HoldingManagerEntry
                {
                    ManagerNumber = 1,
                    ManagerName = "Manager",
                    Shares = 900_000,
                    Value = 45_000_000,
                },
            ],
        };
        await using (var seed = fixture.CreateDbContext())
        {
            holding.Issuer = Equibles
                .TestSupport.NativeListingSeed.ForStock(seed, stock)
                .Security.Issuer;
            seed.Add(holding);
            await seed.SaveChangesAsync();
        }
        var failure = new FailSnapshotRead();
        var contexts = new List<EquiblesFinancialDbContext>();
        var scopes = Substitute.For<IServiceScopeFactory>();
        scopes
            .CreateScope()
            .Returns(_ =>
            {
                var context = fixture.CreateDbContext(options => options.AddInterceptors(failure));
                contexts.Add(context);
                var services = Substitute.For<IServiceProvider>();
                services.GetService(typeof(EquiblesFinancialDbContext)).Returns(context);
                var scope = Substitute.For<IServiceScope>();
                scope.ServiceProvider.Returns(services);
                return scope;
            });
        var repair = new HoldingValueFallbackRepairService(
            scopes,
            Substitute.For<IStockPriceProvider>(),
            NullLogger<HoldingValueFallbackRepairService>.Instance
        );
        try
        {
            await repair.Repair(CancellationToken.None);
            failure.Fired.Should().BeTrue();
            await using (var read = fixture.CreateDbContext())
            {
                var unchanged = await read.Set<InstitutionalHolding>()
                    .Include(h => h.ManagerEntries)
                    .SingleAsync();
                unchanged.Value.Should().Be(45_000_000);
                unchanged.ManagerEntries.Single().Value.Should().Be(45_000_000);
            }
            (await repair.Repair(CancellationToken.None)).Should().Be(1);
            await using var verified = fixture.CreateDbContext();
            (await verified.Set<InstitutionalHolding>().SingleAsync()).Value.Should().Be(45_000);
            (await verified.Set<AumQuarterlySnapshot>().SingleAsync()).DirtyAt.Should().NotBeNull();
        }
        finally
        {
            foreach (var context in contexts)
                await context.DisposeAsync();
        }
    }

    private sealed class FailSnapshotRead : DbCommandInterceptor
    {
        public bool Fired { get; private set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default
        )
        {
            if (!Fired && command.CommandText.Contains("AumQuarterlySnapshot"))
            {
                Fired = true;
                throw new InvalidOperationException("Injected rollup failure after position save");
            }
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
