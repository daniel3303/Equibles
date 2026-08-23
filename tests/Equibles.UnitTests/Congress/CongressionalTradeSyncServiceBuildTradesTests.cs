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
            ),
            Substitute.For<CongressionalFilingLedger>((IServiceScopeFactory)null),
            Substitute.For<CongressionalTradeImportLedger>((IServiceScopeFactory)null),
            Substitute.For<CongressionalTradeIssuerResolver>(null, null)
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
            AssetType = "OP",
            Subholding = "Brokerage IRA",
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
                        new Dictionary<DisclosureTransaction, Guid?> { [tx] = stock.Id },
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
        trade.AssetType.Should().Be("OP");
        trade.Subholding.Should().Be("Brokerage IRA");
        trade.FiledTicker.Should().Be("AAPL");
    }

    [Fact]
    public void BuildTrades_UnresolvedTicker_PreservesSourceFactWithoutCompanyLink()
    {
        var sut = new CongressionalTradeSyncService(
            Substitute.For<IServiceScopeFactory>(),
            Options.Create(new WorkerOptions()),
            Substitute.For<ILogger<CongressionalTradeSyncService>>(),
            Substitute.For<ErrorReporter>(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            ),
            Substitute.For<CongressionalFilingLedger>((IServiceScopeFactory)null),
            Substitute.For<CongressionalTradeImportLedger>((IServiceScopeFactory)null),
            Substitute.For<CongressionalTradeIssuerResolver>(null, null)
        );
        var member = new CongressMember { Id = Guid.NewGuid(), Name = "Jane Smith" };
        var transaction = new DisclosureTransaction
        {
            MemberName = member.Name,
            Ticker = "GOLD",
            AssetName = "Barrick Gold Corporation",
            TransactionType = CongressTransactionType.Purchase,
            TransactionDate = new DateOnly(2021, 1, 5),
            FilingDate = new DateOnly(2021, 1, 12),
            SourceId = "20018567",
            FilingKind = CongressionalFilingKind.HousePeriodicTransactionReport,
            SourceRowIndex = 3,
        };

        var trades = sut.BuildTrades(
            [transaction],
            new Dictionary<string, CongressMember> { [member.Name] = member },
            new Dictionary<DisclosureTransaction, Guid?> { [transaction] = null }
        );

        var trade = trades.Should().ContainSingle().Which;
        trade.CommonStockId.Should().BeNull();
        trade.FiledTicker.Should().Be("GOLD");
        trade.SourceId.Should().Be("20018567");
        trade.SourceRowIndex.Should().Be(3);
        trade.FilingKind.Should().Be(CongressionalFilingKind.HousePeriodicTransactionReport);
    }
}
