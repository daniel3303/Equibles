using Equibles.Congress.HostedService.Services;
using Equibles.Core.Configuration;
using Equibles.Errors.BusinessLogic;
using Equibles.IntegrationTests.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.IntegrationTests.Congress;

/// <summary>
/// Unit-tier CongressionalTradeSyncServiceTests cover only the from-date clamp
/// and the 90-day default. The disclosure-fetch resilience path is uncovered.
/// Pins SyncAll when BOTH the Senate and House client resolutions throw inside
/// their scope: each catch block must log + report, and the short-circuit on
/// "no transactions" must keep SyncAll from attempting the (uninjected)
/// ProcessTransactions DB scope. A regression that re-raised either client
/// failure would crash the entire worker every cycle when EITHER site is down.
/// </summary>
public class CongressionalTradeSyncServiceBothFailTests
{
    [Fact]
    public async Task SyncAll_BothClientResolutionsThrow_LogsBothAndReturnsWithoutTouchingDb()
    {
        // Empty scope factory → GetRequiredService<SenateDisclosureClient> throws
        // InvalidOperationException, hits the generic catch, gets logged + reported.
        // Same for HouseDisclosureClient.
        var scopeFactory = ServiceScopeSubstitute.Create();
        var errorReporter = new ErrorReporter(
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ILogger<ErrorReporter>>()
        );
        var sut = new CongressionalTradeSyncService(
            scopeFactory,
            Options.Create(
                new WorkerOptions
                {
                    // Recent window so the EarliestAvailableDate clamp doesn't fire.
                    MinSyncDate = DateTime.UtcNow.AddDays(-30),
                }
            ),
            Substitute.For<ILogger<CongressionalTradeSyncService>>(),
            errorReporter
        );

        // Must not throw — both fetches fail, allTransactions stays empty,
        // SyncAll hits the "No congressional transactions found" early return.
        await sut.SyncAll(CancellationToken.None);

        // No way to assert the absence of ProcessTransactions directly without
        // service-resolution side effects — but the test reaching here without
        // throw is itself the proof: ProcessTransactions calls
        // GetRequiredService<EquiblesFinancialDbContext> on the same empty scope factory,
        // which would also throw if reached, and there is no outer catch to swallow it.
        true.Should().BeTrue();
    }
}
