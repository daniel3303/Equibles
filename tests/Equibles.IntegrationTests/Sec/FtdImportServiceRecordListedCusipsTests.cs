using System.Reflection;
using Equibles.CommonStocks.BusinessLogic;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.Configuration;
using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.Integrations.Sec.Contracts;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.HostedService.Services;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Equibles.IntegrationTests.Sec;

/// <summary>
/// The listed-CUSIP sweep's issuer-prefix guard (#4247). The sweep resolves CNS symbols
/// against the SECONDARY ticker space, and the only thing standing between a recycled
/// secondary symbol and importing a delisted issuer's positions is
/// <c>CusipIdentity.SameIssuer</c> against the stock's primary CUSIP. Sibling classes
/// SHARE the first-six issuer prefix — for this table that is the admission ticket, not
/// a trap. A stock with no primary CUSIP has no prefix to verify against and must
/// record nothing.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class FtdImportServiceRecordListedCusipsTests : IAsyncLifetime
{
    private readonly ParadeDbFixture _fixture;
    private readonly List<EquiblesFinancialDbContext> _contexts = [];

    public FtdImportServiceRecordListedCusipsTests(ParadeDbFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetAsync();

    public Task DisposeAsync()
    {
        foreach (var ctx in _contexts)
            ctx.Dispose();
        return Task.CompletedTask;
    }

    private EquiblesFinancialDbContext FreshContext()
    {
        var ctx = _fixture.CreateDbContext();
        _contexts.Add(ctx);
        return ctx;
    }

    private FtdImportService BuildService()
    {
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory
            .CreateScope()
            .Returns(_ =>
            {
                var ctx = FreshContext();
                var sp = Substitute.For<IServiceProvider>();
                sp.GetService(typeof(CommonStockRepository))
                    .Returns(new CommonStockRepository(ctx));
                sp.GetService(typeof(CommonStockManager))
                    .Returns(
                        new CommonStockManager(
                            new CommonStockRepository(ctx),
                            Substitute.For<IBus>()
                        )
                    );
                var scope = Substitute.For<IServiceScope>();
                scope.ServiceProvider.Returns(sp);
                return scope;
            });

        return new FtdImportService(
            scopeFactory,
            Substitute.For<ISecEdgarClient>(),
            Substitute.For<ILogger<FtdImportService>>(),
            new ErrorReporter(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            ),
            Options.Create(new WorkerOptions())
        );
    }

    private static async Task<int> InvokeRecordListedCusips(
        FtdImportService sut,
        Dictionary<Guid, Dictionary<string, HashSet<string>>> byStock
    )
    {
        var method = typeof(FtdImportService).GetMethod(
            "RecordListedCusips",
            BindingFlags.NonPublic | BindingFlags.Instance
        )!;
        return await (Task<int>)method.Invoke(sut, [byStock, CancellationToken.None])!;
    }

    private static Dictionary<Guid, Dictionary<string, HashSet<string>>> Candidates(
        Guid stockId,
        string listedTicker,
        params string[] cusips
    ) =>
        new()
        {
            [stockId] = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase)
            {
                [listedTicker] = new HashSet<string>(cusips, StringComparer.OrdinalIgnoreCase),
            },
        };

    [Fact]
    public async Task RecordListedCusips_SiblingClassSharesIssuerPrefix_Records()
    {
        var alphabet = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "GOOGL",
            Name = "Alphabet Inc",
            Cik = "1652044",
            Cusip = "02079K305",
            SecondaryTickers = ["GOOG"],
        };
        await using (var seed = _fixture.CreateDbContext())
        {
            seed.Set<CommonStock>().Add(alphabet);
            await seed.SaveChangesAsync();
        }

        var recorded = await InvokeRecordListedCusips(
            BuildService(),
            Candidates(alphabet.Id, "GOOG", "02079K107")
        );

        recorded.Should().Be(1);
        using var verify = FreshContext();
        var listing = await verify.Set<CommonStockListedCusip>().SingleAsync();
        listing.CommonStockId.Should().Be(alphabet.Id);
        listing.ListedTicker.Should().Be("GOOG");
        listing.Cusip.Should().Be("02079K107");
    }

    [Fact]
    public async Task RecordListedCusips_ForeignIssuerPrefix_RecordsNothing()
    {
        // The recycled-symbol case: an old archive file pairs this secondary symbol with a
        // DIFFERENT issuer's CUSIP (the symbol's previous owner). Without the prefix guard
        // that delisted issuer's 13F lines would import as this filer's sibling class.
        var stock = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "AAA",
            Name = "Current Symbol Owner Inc",
            Cik = "0000000001",
            Cusip = "111111111",
            SecondaryTickers = ["AAA-A"],
        };
        await using (var seed = _fixture.CreateDbContext())
        {
            seed.Set<CommonStock>().Add(stock);
            await seed.SaveChangesAsync();
        }

        var recorded = await InvokeRecordListedCusips(
            BuildService(),
            Candidates(stock.Id, "AAA-A", "999999109")
        );

        recorded.Should().Be(0);
        using var verify = FreshContext();
        (await verify.Set<CommonStockListedCusip>().AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task RecordListedCusips_StockWithoutPrimaryCusip_RecordsNothing()
    {
        // No primary CUSIP means no issuer prefix to anchor the guard — unverifiable
        // identity is skipped, not admitted on faith.
        var stock = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "BBB",
            Name = "Unseeded Issuer Inc",
            Cik = "0000000002",
            SecondaryTickers = ["BBB-A"],
        };
        await using (var seed = _fixture.CreateDbContext())
        {
            seed.Set<CommonStock>().Add(stock);
            await seed.SaveChangesAsync();
        }

        var recorded = await InvokeRecordListedCusips(
            BuildService(),
            Candidates(stock.Id, "BBB-A", "22222R107")
        );

        recorded.Should().Be(0);
        using var verify = FreshContext();
        (await verify.Set<CommonStockListedCusip>().AnyAsync()).Should().BeFalse();
    }
}
