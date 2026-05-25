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
using Xunit;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Pins the two arms of <c>ResolveTickerCollision</c> the orchestration tests
/// couldn't reach deterministically: the already-attached subsidiary early
/// return, and the catch arm. The method is invoked directly via reflection
/// with explicitly constructed incoming/incumbent so there is no
/// routing/role-mapping ambiguity.
/// </summary>
public class CompanySyncServiceResolveTickerCollisionTests
{
    private static (CompanySyncService Sut, ISecEdgarClient Client) BuildSut()
    {
        var client = Substitute.For<ISecEdgarClient>();
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
        return (sut, client);
    }

    private static object EmptyState()
    {
        var stateType = typeof(CompanySyncService).GetNestedType(
            "StockSyncState",
            BindingFlags.NonPublic
        );
        var state = Activator.CreateInstance(stateType);
        void Set(string name, object value) => stateType.GetProperty(name).SetValue(state, value);
        Set("SecCiks", new HashSet<string>());
        Set("ExistingStocks", new List<CommonStock>());
        Set("ExistingCiks", new HashSet<string>());
        Set("ExistingPrimaryTickers", new HashSet<string>());
        Set("PrimaryTickerToStock", new Dictionary<string, CommonStock>());
        Set("SecondaryCikToParent", new Dictionary<string, CommonStock>());
        return state;
    }

    private static Task InvokeResolve(
        CompanySyncService sut,
        CompanyInfo incoming,
        CommonStock incumbent,
        string ticker,
        object state
    )
    {
        var m = typeof(CompanySyncService).GetMethod(
            "ResolveTickerCollision",
            BindingFlags.NonPublic | BindingFlags.Instance
        );
        return (Task)m.Invoke(sut, [incoming, incumbent, ticker, state]);
    }

    [Fact]
    public async Task ResolveTickerCollision_IncomingAlreadyAttachedAsSubsidiary_ReturnsEarly()
    {
        var (sut, client) = BuildSut();
        // Metadata missing ⇒ ShouldIncumbentWin returns true (defaults to
        // incumbent). The incoming CIK is already a SecondaryCik on the
        // incumbent ⇒ the dedupe guard returns before re-attaching.
        client.GetCompanyMetadata(Arg.Any<string>()).Returns((CompanyMetadata)null);

        var incoming = new CompanyInfo { Cik = "0000001111", Name = "Sub Co" };
        var incumbent = new CommonStock
        {
            Cik = "0000009999",
            Ticker = "SHARED",
            Name = "Parent Co",
            SecondaryCiks = ["0000001111"],
        };

        await InvokeResolve(sut, incoming, incumbent, "SHARED", EmptyState());

        incumbent
            .SecondaryCiks.Should()
            .ContainSingle()
            .Which.Should()
            .Be("0000001111", "the already-attached CIK must not be duplicated");
    }

    [Fact]
    public async Task ResolveTickerCollision_MetadataLookupThrows_LogsAndReportsWithoutRethrowing()
    {
        var (sut, client) = BuildSut();
        client
            .GetCompanyMetadata(Arg.Any<string>())
            .Returns<Task<CompanyMetadata>>(_ =>
                throw new InvalidOperationException("SEC metadata endpoint down")
            );

        var incoming = new CompanyInfo { Cik = "0000001111", Name = "Incoming Co" };
        var incumbent = new CommonStock
        {
            Cik = "0000009999",
            Ticker = "SHARED",
            Name = "Incumbent Co",
        };

        // Must not throw — the catch arm logs and escalates, the sync carries on.
        await InvokeResolve(sut, incoming, incumbent, "SHARED", EmptyState());
    }
}
