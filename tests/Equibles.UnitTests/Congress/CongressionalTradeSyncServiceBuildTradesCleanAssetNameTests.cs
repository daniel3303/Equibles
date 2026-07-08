using Equibles.CommonStocks.Data.Models;
using Equibles.Congress.Data.Models;
using Equibles.Congress.HostedService.Models;
using Equibles.Congress.HostedService.Services;
using Equibles.Core.Configuration;
using Equibles.Errors.BusinessLogic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.UnitTests.Congress;

// The stored AssetName is part of the congressional-trade upsert unique key, so BuildTrades
// must be the normalization choke point: a transaction whose emitter forgot to clean the name
// (or a stored variant produced by an older normalization) must never reach the upsert with a
// dirty name — a mismatch there re-inserts the same trade as a duplicate row. The #4111
// CleanAssetName rollout duplicated 8,104 production trades exactly this way, because the
// scrapers started emitting cleaned names that no longer matched the stored dirty ones.
public class CongressionalTradeSyncServiceBuildTradesCleanAssetNameTests
{
    [Theory]
    [InlineData(
        "Space Exploration Technologies Corp.  - Class A Common Stock (SPCX)",
        "Space Exploration Technologies Corp. - Class A Common Stock (SPCX)"
    )]
    [InlineData("Weyerhaeuser Company (WY) gfedcb", "Weyerhaeuser Company (WY)")]
    [InlineData("Apple Inc. (AAPL)", "Apple Inc. (AAPL)")]
    public void BuildTrades_NormalizesAssetNameForTheUpsertKey(string raw, string expected)
    {
        var sut = CreateService();
        var member = new CongressMember { Name = "Jane Doe" };
        var stock = new CommonStock();
        var transaction = new DisclosureTransaction
        {
            MemberName = "Jane Doe",
            Ticker = "WY",
            AssetName = raw,
            TransactionDate = new DateOnly(2026, 6, 18),
            FilingDate = new DateOnly(2026, 7, 2),
            TransactionType = CongressTransactionType.Purchase,
            AmountFrom = 1001,
            AmountTo = 15000,
        };

        var trades = sut.BuildTrades(
            [transaction],
            new Dictionary<string, CongressMember> { ["Jane Doe"] = member },
            new Dictionary<string, CommonStock> { ["WY"] = stock }
        );

        trades.Should().ContainSingle().Which.AssetName.Should().Be(expected);
    }

    [Fact]
    public void BuildTrades_NullAssetName_StoresEmptyString()
    {
        var sut = CreateService();
        var member = new CongressMember { Name = "Jane Doe" };
        var transaction = new DisclosureTransaction
        {
            MemberName = "Jane Doe",
            Ticker = "WY",
            AssetName = null,
            TransactionDate = new DateOnly(2026, 6, 18),
            FilingDate = new DateOnly(2026, 7, 2),
            TransactionType = CongressTransactionType.Purchase,
        };

        var trades = sut.BuildTrades(
            [transaction],
            new Dictionary<string, CongressMember> { ["Jane Doe"] = member },
            new Dictionary<string, CommonStock> { ["WY"] = new CommonStock() }
        );

        trades.Should().ContainSingle().Which.AssetName.Should().Be("");
    }

    private static CongressionalTradeSyncService CreateService()
    {
        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(Substitute.For<IServiceProvider>());
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(scope);
        var errorReporter = Substitute.For<ErrorReporter>(
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ILogger<ErrorReporter>>()
        );

        return new CongressionalTradeSyncService(
            scopeFactory,
            Options.Create(new WorkerOptions()),
            Substitute.For<ILogger<CongressionalTradeSyncService>>(),
            errorReporter
        );
    }
}
