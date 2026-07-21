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
/// Adversarial boundary: the future-date guard drops a trade only when
/// <c>TransactionDate &gt; FilingDate</c> (strict). A same-day disclosure
/// (transaction filed the day it executed) sits exactly on the boundary and must be
/// KEPT — a common, legitimate case. The existing future-date suite pins "after" (skip)
/// and strictly-before (build), but not the equal-date edge that a <c>&gt;</c>→<c>&gt;=</c>
/// regression would silently start dropping.
/// </summary>
public class CongressionalTradeSyncServiceBuildTradesSameDayTests
{
    [Fact]
    public void BuildTrades_TransactionDateEqualsFilingDate_BuildsTrade()
    {
        var sut = new CongressionalTradeSyncService(
            Substitute.For<IServiceScopeFactory>(),
            Options.Create(new WorkerOptions()),
            Substitute.For<ILogger<CongressionalTradeSyncService>>(),
            Substitute.For<ErrorReporter>(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            ),
            Substitute.For<CongressionalFilingLedger>((IServiceScopeFactory)null)
        );
        var stock = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "IBM",
            Name = "International Business Machines",
            Cik = "0000051143",
        };
        var member = new CongressMember { Id = Guid.NewGuid(), Name = "Pete Sessions" };
        var sameDay = new DateOnly(2021, 5, 3);
        var tx = new DisclosureTransaction
        {
            MemberName = member.Name,
            Ticker = stock.Ticker,
            AssetName = "International Business Machines Corporation (IBM)",
            TransactionType = CongressTransactionType.Purchase,
            OwnerType = "SP",
            TransactionDate = sameDay,
            FilingDate = sameDay,
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
                        new Dictionary<string, CongressMember> { [member.Name] = member },
                        new Dictionary<string, CommonStock> { [stock.Ticker] = stock },
                    ]
                );

        trades.Should().ContainSingle().Which.TransactionDate.Should().Be(sameDay);
    }
}
