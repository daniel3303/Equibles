using System.Reflection;
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

/// <summary>
/// BuildTrades maps matched disclosures to CongressionalTrade rows. House PTRs
/// routinely omit the asset description, so AssetName arrives null; the trade's
/// AssetName column is non-nullable. The `?? ""` coalesce is the only thing
/// stopping a null from reaching the DB as a constraint violation that aborts
/// the whole persist batch. No existing test feeds a null AssetName.
/// </summary>
public class CongressionalTradeSyncServiceBuildTradesTests
{
    [Fact]
    public void BuildTrades_TransactionWithNullAssetName_MapsToEmptyStringNotNull()
    {
        var sut = new CongressionalTradeSyncService(
            Substitute.For<IServiceScopeFactory>(),
            Options.Create(new WorkerOptions()),
            Substitute.For<ILogger<CongressionalTradeSyncService>>(),
            Substitute.For<ErrorReporter>(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            )
        );

        var member = new CongressMember { Id = Guid.NewGuid(), Name = "Jane Smith" };
        var stock = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "AAPL",
            Name = "Apple Inc",
            Cik = "0000320193",
        };
        var tx = new DisclosureTransaction
        {
            MemberName = "Jane Smith",
            Ticker = "AAPL",
            AssetName = null,
            TransactionType = CongressTransactionType.Purchase,
            OwnerType = "SP",
            TransactionDate = new DateOnly(2025, 1, 14),
            FilingDate = new DateOnly(2025, 1, 20),
            AmountFrom = 1001,
            AmountTo = 15000,
        };

        var method = typeof(CongressionalTradeSyncService).GetMethod(
            "BuildTrades",
            BindingFlags.NonPublic | BindingFlags.Instance
        );
        var trades =
            (List<CongressionalTrade>)
                method.Invoke(
                    sut,
                    [
                        new List<DisclosureTransaction> { tx },
                        new Dictionary<string, CongressMember> { ["Jane Smith"] = member },
                        new Dictionary<string, CommonStock> { ["AAPL"] = stock },
                    ]
                );

        trades.Should().ContainSingle();
        var trade = trades[0];
        trade
            .AssetName.Should()
            .Be("", "a null asset name must be coalesced, never persisted as null");
        trade.CongressMemberId.Should().Be(member.Id);
        trade.CommonStockId.Should().Be(stock.Id);
        trade.AmountFrom.Should().Be(1001);
        trade.AmountTo.Should().Be(15000);
    }
}
