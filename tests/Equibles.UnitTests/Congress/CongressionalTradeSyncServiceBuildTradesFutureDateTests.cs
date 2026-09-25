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
            ),
            Substitute.For<CongressionalFilingLedger>((IServiceScopeFactory)null),
            Substitute.For<CongressionalTradeImportLedger>((IServiceScopeFactory)null),
            Substitute.For<CongressionalTradeIssuerResolver>(null, null),
            Substitute.For<ICongressMemberIdentityService>()
        );

    private static List<CongressionalTrade> InvokeBuildTrades(
        CongressionalTradeSyncService sut,
        DisclosureTransaction tx,
        EquityIssuer stock,
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
                    new Dictionary<DisclosureTransaction, Guid?> { [tx] = stock.Id },
                ]
            );
    }

    private static (EquityIssuer, CongressMember) Fixtures()
    {
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Id: Guid.NewGuid(),
            Ticker: "IBM",
            Name: "International Business Machines",
            Cik: "0000051143"
        );
        var member = new CongressMember { Id = Guid.NewGuid(), Name = "Pete Sessions" };
        return (stock, member);
    }

    // Rows dated after their filing are dropped earlier, in ProcessTransactions (see the
    // integration tests); BuildTrades builds every row it is given. A normal trade (disclosed on or after the transaction) is still built.
    [Fact]
    public void BuildTrades_TransactionDateOnOrBeforeFilingDate_BuildsTrade()
    {
        var sut = CreateSut();
        var (stock, member) = Fixtures();
        var tx = new DisclosureTransaction
        {
            MemberName = member.Name,
            Ticker = stock.Presentation.Listing.Ticker,
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
