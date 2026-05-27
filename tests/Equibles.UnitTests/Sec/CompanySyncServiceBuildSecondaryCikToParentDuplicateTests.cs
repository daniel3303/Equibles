using System.Reflection;
using Equibles.CommonStocks.Data.Models;
using Equibles.Core.Configuration;
using Equibles.Errors.BusinessLogic;
using Equibles.Integrations.Sec.Contracts;
using Equibles.Sec.HostedService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.UnitTests.Sec;

public class CompanySyncServiceBuildSecondaryCikToParentDuplicateTests
{
    private static readonly MethodInfo BuildSecondaryCikToParentMethod =
        typeof(CompanySyncService).GetMethod(
            "BuildSecondaryCikToParent",
            BindingFlags.NonPublic | BindingFlags.Instance
        );

    // The XML doc on BuildSecondaryCikToParent commits to a precise defensive
    // contract: "Built defensively to survive a data anomaly (the same
    // subsidiary CIK attached to two parents) rather than throwing", and the
    // warning template says "keeping {ExistingParent}". So a duplicate
    // subsidiary CIK must (1) not throw and (2) preserve the FIRST parent in
    // iteration order. A refactor that switched TryAdd to indexer-assign (a
    // common simplification) would silently flip "keep existing" to "last
    // write wins", silently swapping subsidiary ownership on every sync.
    [Fact]
    public void BuildSecondaryCikToParent_TwoParentsShareSubsidiaryCik_KeepsFirstParentSilently()
    {
        var apple = new CommonStock
        {
            Ticker = "AAPL",
            Cik = "0000320193",
            SecondaryCiks = ["0000999999"],
        };
        var microsoft = new CommonStock
        {
            Ticker = "MSFT",
            Cik = "0000789019",
            SecondaryCiks = ["0000999999"],
        };
        var service = CreateService();

        var result =
            (Dictionary<string, CommonStock>)
                BuildSecondaryCikToParentMethod.Invoke(
                    service,
                    [new List<CommonStock> { apple, microsoft }]
                );

        result["0000999999"].Should().BeSameAs(apple);
    }

    private static CompanySyncService CreateService()
    {
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        var secEdgarClient = Substitute.For<ISecEdgarClient>();
        var options = Options.Create(new WorkerOptions());
        var logger = Substitute.For<ILogger<CompanySyncService>>();
        var errorReporter = Substitute.For<ErrorReporter>(
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ILogger<ErrorReporter>>()
        );
        return new CompanySyncService(scopeFactory, secEdgarClient, options, logger, errorReporter);
    }
}
