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

public class CongressionalTradeSyncServiceBuildTradesMissingMemberTests
{
    private static CongressionalTradeSyncService CreateSut() =>
        new(
            Substitute.For<IServiceScopeFactory>(),
            Options.Create(new WorkerOptions()),
            Substitute.For<ILogger<CongressionalTradeSyncService>>(),
            Substitute.For<ErrorReporter>(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            ),
            Substitute.For<CongressionalFilingLedger>((IServiceScopeFactory)null)
        );

    private static List<CongressionalTrade> InvokeBuildTrades(
        CongressionalTradeSyncService sut,
        DisclosureTransaction tx,
        Dictionary<string, CongressMember> members,
        CommonStock stock
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
                    members,
                    new Dictionary<string, CommonStock> { [stock.Ticker] = stock },
                ]
            );
    }

    // Contract: a transaction survives the stock match but its member name is absent from the
    // post-upsert member dictionary (an upsert/re-query mismatch). BuildTrades must skip such a
    // trade gracefully — never index the missing key, throw, or emit a row — so one orphaned
    // member can't abort the whole batch. The stock lookup below it uses an unchecked indexer,
    // so the early member guard is the only thing standing between a name miss and a crash.
    [Fact]
    public void BuildTrades_MemberMissingFromUpsertedSet_SkipsTradeWithoutThrowing()
    {
        var sut = CreateSut();
        var stock = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "IBM",
            Name = "International Business Machines",
            Cik = "0000051143",
        };
        var tx = new DisclosureTransaction
        {
            MemberName = "Unmatched Member",
            Ticker = stock.Ticker,
            AssetName = "International Business Machines Corporation (IBM)",
            TransactionType = CongressTransactionType.Purchase,
            OwnerType = "SP",
            TransactionDate = new DateOnly(2021, 4, 30),
            FilingDate = new DateOnly(2021, 5, 3),
            AmountFrom = 1001,
            AmountTo = 15000,
        };

        var trades = InvokeBuildTrades(sut, tx, new Dictionary<string, CongressMember>(), stock);

        trades.Should().BeEmpty();
    }
}
