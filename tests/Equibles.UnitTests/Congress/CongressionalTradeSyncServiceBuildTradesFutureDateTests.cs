using System.Reflection;
using Equibles.CommonStocks.Data.Models;
using Equibles.Congress.Data.Models;
using Equibles.Congress.HostedService.Models;
using Equibles.Congress.HostedService.Services;
using Equibles.Core.Configuration;
using Equibles.Errors.BusinessLogic;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.UnitTests.Congress;

public class CongressionalTradeSyncServiceBuildTradesFutureDateTests
{
    private static CongressionalTradeSyncService CreateSut() =>
        new(
            Substitute.For<IServiceScopeFactory>(),
            Options.Create(new WorkerOptions()),
            Substitute.For<ILogger<CongressionalTradeSyncService>>(),
            Substitute.For<ErrorReporter>(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            )
        );

    private static List<CongressionalTrade> InvokeBuildTrades(
        CongressionalTradeSyncService sut,
        DisclosureTransaction tx,
        CommonStock stock,
        CongressMember member
    )
    {
        var method = typeof(CongressionalTradeSyncService).GetMethod(
            "BuildTrades",
            BindingFlags.NonPublic | BindingFlags.Instance
        );
        return (List<CongressionalTrade>)
            method.Invoke(
                sut,
                [
                    new List<DisclosureTransaction> { tx },
                    new Dictionary<string, CongressMember> { [member.Name] = member },
                    new Dictionary<string, CommonStock> { [stock.Ticker] = stock },
                ]
            );
    }

    private static (CommonStock, CongressMember) Fixtures()
    {
        var stock = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "IBM",
            Name = "International Business Machines",
            Cik = "0000051143",
        };
        var member = new CongressMember { Id = Guid.NewGuid(), Name = "Pete Sessions" };
        return (stock, member);
    }

    // A trade is always disclosed after it happens, so the transaction date can never be after
    // the filing date. A source typo (e.g. year 3031) that violates this must be dropped, not
    // stored — otherwise it sorts to the top of the member's newest-first trade history.
    [Fact]
    public void BuildTrades_TransactionDateAfterFilingDate_SkipsTrade()
    {
        var sut = CreateSut();
        var (stock, member) = Fixtures();
        var tx = new DisclosureTransaction
        {
            MemberName = member.Name,
            Ticker = stock.Ticker,
            AssetName = "International Business Machines Corporation (IBM)",
            TransactionType = CongressTransactionType.Purchase,
            OwnerType = "SP",
            TransactionDate = new DateOnly(3031, 4, 30),
            FilingDate = new DateOnly(2021, 5, 3),
            AmountFrom = 1001,
            AmountTo = 15000,
        };

        var trades = InvokeBuildTrades(sut, tx, stock, member);

        trades.Should().BeEmpty();
    }

    // A normal trade (disclosed on or after the transaction) is still built.
    [Fact]
    public void BuildTrades_TransactionDateOnOrBeforeFilingDate_BuildsTrade()
    {
        var sut = CreateSut();
        var (stock, member) = Fixtures();
        var tx = new DisclosureTransaction
        {
            MemberName = member.Name,
            Ticker = stock.Ticker,
            AssetName = "International Business Machines Corporation (IBM)",
            TransactionType = CongressTransactionType.Purchase,
            OwnerType = "SP",
            TransactionDate = new DateOnly(2021, 4, 30),
            FilingDate = new DateOnly(2021, 5, 3),
            AmountFrom = 1001,
            AmountTo = 15000,
        };

        var trades = InvokeBuildTrades(sut, tx, stock, member);

        trades.Should().HaveCount(1);
        trades[0].TransactionDate.Should().Be(new DateOnly(2021, 4, 30));
    }
}
