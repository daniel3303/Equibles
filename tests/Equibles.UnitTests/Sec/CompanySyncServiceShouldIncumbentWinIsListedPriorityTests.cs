using System.Reflection;
using Equibles.CommonStocks.Data.Models;
using Equibles.Core.Configuration;
using Equibles.Errors.BusinessLogic;
using Equibles.Integrations.Sec.Contracts;
using Equibles.Integrations.Sec.Models;
using Equibles.Sec.HostedService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Sibling to the ResolveTickerCollision pins (AlreadyAttached / MetadataThrows)
/// which exercise the orchestration entry point. This pins the highest-priority
/// arm of the inner <c>ShouldIncumbentWin</c> tie-breaker: the XML doc on
/// <c>ResolveTickerCollision</c> declares the priority as
/// "listed-on-exchange &gt; operating &gt; older CIK". A refactor that
/// reverses or drops the listing comparison would silently keep an unlisted
/// subsidiary as the ticker's rightful owner whenever the actual listed
/// filer joins the feed — corrupting every downstream lookup that trusts the
/// primary-ticker assignment.
/// </summary>
public class CompanySyncServiceShouldIncumbentWinIsListedPriorityTests
{
    [Fact]
    public async Task ShouldIncumbentWin_IncomingListedIncumbentUnlisted_ReturnsFalseSoIncumbentLoses()
    {
        var client = Substitute.For<ISecEdgarClient>();
        // Incumbent: operating but NOT listed (empty Exchanges).
        // Incoming: operating AND listed on NASDAQ.
        // Per the documented "listed > operating > older CIK" priority, the
        // listed side must win regardless of who's currently the incumbent.
        client
            .GetCompanyMetadata("0000001111")
            .Returns(
                new CompanyMetadata
                {
                    Cik = "0000001111",
                    EntityType = "operating",
                    Exchanges = ["NASDAQ"],
                }
            );
        client
            .GetCompanyMetadata("0000009999")
            .Returns(
                new CompanyMetadata
                {
                    Cik = "0000009999",
                    EntityType = "operating",
                    Exchanges = [],
                }
            );

        var sut = new CompanySyncService(
            Substitute.For<IServiceScopeFactory>(),
            client,
            Options.Create(new WorkerOptions()),
            Substitute.For<ILogger<CompanySyncService>>(),
            new ErrorReporter(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            )
        );

        var incoming = new CompanyInfo { Cik = "0000001111", Name = "Incoming Co" };
        var incumbent = new CommonStock
        {
            Cik = "0000009999",
            Ticker = "SHARED",
            Name = "Incumbent Co",
        };

        var method = typeof(CompanySyncService).GetMethod(
            "ShouldIncumbentWin",
            BindingFlags.NonPublic | BindingFlags.Instance
        );
        var result = await (Task<bool>)method!.Invoke(sut, [incoming, incumbent]);

        result
            .Should()
            .BeFalse(
                "listed-on-exchange is the top priority signal — when only the incoming side is listed, the incumbent must lose the ticker"
            );
    }
}
