using Equibles.Sec.FinancialFacts.HostedService.Extensions;
using Equibles.Sec.FinancialFacts.HostedService.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Equibles.UnitTests.Sec;

public class SecFinancialFactsServiceCollectionExtensionsAutoWiresTests
{
    // Sibling to SecFinancialFactsServiceCollectionExtensionsTests
    // (#2413, hosted-service registration). Mirrors the FRED-module
    // pattern: AddFredWorker has TWO registration pins —
    // AutoWiresFredImportService (canonical [Service] smoke test)
    // and RegistersFredScraperWorkerAsIHostedService. The
    // FinancialFacts module now has the hosted-service pin; this
    // pin completes the pair by covering the AutoWires scan.
    //
    // Contract (ServiceCollectionExtensions.cs):
    //   services.AutoWireServicesFrom<FinancialFactsImportService>();
    //   services.AddHostedService<FinancialFactsScraperWorker>();
    //
    // FinancialFactsImportService is the canonical [Service]-tagged
    // class in the Sec.FinancialFacts.HostedService assembly. The
    // AutoWireServicesFrom<TMarker> call reflects on TMarker's
    // assembly and wires every [Service] attribute it finds. A
    // regression that:
    //
    //   • Drops AutoWireServicesFrom entirely — FinancialFactsImportService
    //     and every other [Service] in the assembly (TickerMapService
    //     usage, etc.) becomes unresolvable at runtime; the
    //     BackgroundService fails to resolve its primary collaborator
    //     and crashes on first cycle.
    //
    //   • Swaps the marker to a type from a DIFFERENT assembly (e.g.
    //     a copy-paste from AddFredWorker that left
    //     `AutoWireServicesFrom<FredImportService>` accidentally) —
    //     the wrong assembly is scanned; FinancialFactsImportService is
    //     missed even though the assembly-of-the-correct-type WOULD
    //     have been scanned.
    //
    //   • Swaps to a non-[Service] type from the right assembly —
    //     auto-wire mechanically still walks the right assembly, but
    //     a future refactor that removes the [Service] attribute from
    //     FinancialFactsImportService specifically would cause the
    //     class to not be registered; the AutoWires call would still
    //     "work" mechanically. The pin asserts FinancialFactsImportService
    //     itself is registered as the smoke test for "the canonical
    //     class actually got wired".
    //
    // Pin: invoke `AddSecFinancialFactsWorker()` and assert the
    // ServiceCollection contains a descriptor with
    // `ServiceType == typeof(FinancialFactsImportService)`. Mirrors
    // the FRED module's AddFredWorker_AutoWiresFredImportService
    // pin shape verbatim.
    [Fact]
    public void AddSecFinancialFactsWorker_AutoWiresFinancialFactsImportService()
    {
        var services = new ServiceCollection();

        services.AddSecFinancialFactsWorker();

        services.Should().Contain(d => d.ServiceType == typeof(FinancialFactsImportService));
    }
}
