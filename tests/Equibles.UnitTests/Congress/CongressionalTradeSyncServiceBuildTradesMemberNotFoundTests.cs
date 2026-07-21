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

public class CongressionalTradeSyncServiceBuildTradesMemberNotFoundTests
{
    // BuildTrades looks up each matched transaction's member with TryGetValue and, on a miss
    // (a member the upsert didn't land), logs and skips it. A skipped trade must NOT be built —
    // never fabricated with a default member id, never an NRE. The happy-path member here is
    // absent from the map, so the row drops and the result is empty. Oracle from the contract.
    [Fact]
    public void BuildTrades_MemberNotInUpsertedMap_SkipsTradeInsteadOfBuildingIt()
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
            Ticker = "AAPL",
            Name = "Apple Inc",
            Cik = "0000320193",
        };
        var tx = new DisclosureTransaction
        {
            MemberName = "Ghost Member",
            Ticker = "AAPL",
            AssetName = "Apple Inc",
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
                        new Dictionary<string, CongressMember>(),
                        new Dictionary<string, CommonStock> { ["AAPL"] = stock },
                    ]
                );

        trades.Should().BeEmpty();
    }
}
