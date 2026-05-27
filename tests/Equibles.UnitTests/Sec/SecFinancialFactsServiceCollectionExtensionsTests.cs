using Equibles.Sec.FinancialFacts.HostedService;
using Equibles.Sec.FinancialFacts.HostedService.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Equibles.UnitTests.Sec;

public class SecFinancialFactsServiceCollectionExtensionsTests
{
    // Mirrors AddFredWorker_RegistersFredScraperWorkerAsIHostedService
    // (#2403's family) for the FinancialFacts module. No existing test
    // covers AddSecFinancialFactsWorker — a regression that drops the
    // `AddHostedService<FinancialFactsScraperWorker>()` call would
    // compile cleanly and silently disable the entire SEC Company
    // Facts ingest at startup. The application boots, every dependency
    // resolves, but no IHostedService implementation of
    // FinancialFactsScraperWorker is enumerated — the per-company
    // EDGAR fact-walker never fires. The financial-statement
    // dashboard's freshness silently freezes at whichever day the
    // regression deployed.
    //
    // The risks this pin uniquely catches:
    //
    //   • Drop of `AddHostedService<FinancialFactsScraperWorker>()` —
    //     a "refactor the AutoWires together" cleanup that removed the
    //     explicit hosted-service registration under the false
    //     intuition that AutoWireServicesFrom would pick up the worker
    //     class too (AutoWireServicesFrom only wires `[Service]`-tagged
    //     classes; BackgroundService subclasses must be registered
    //     explicitly).
    //
    //   • Downgrade to AddScoped/AddSingleton/AddTransient —
    //     `services.AddTransient<FinancialFactsScraperWorker>()`
    //     would register the worker as a resolvable service but NOT
    //     enumerate it as IHostedService. The application boots, the
    //     dependency-resolution scan picks the worker up, but the
    //     generic host never starts it.
    //
    //   • Swap to the wrong worker type — `AddHostedService<
    //     FtdScraperWorker>()` from a copy-paste edit. The hosted-
    //     service list would have the wrong implementation; no
    //     FinancialFactsScraperWorker would run. Asserting on the
    //     exact ImplementationType catches this.
    //
    // This is the cross-module "scraper module DI wiring" pin pattern
    // already in place for FRED / SEC / Holdings / CFTC / Congress /
    // Yahoo / FINRA / Cboe. FinancialFacts was the last unpinned
    // module in the family.
    [Fact]
    public void AddSecFinancialFactsWorker_RegistersFinancialFactsScraperWorkerAsIHostedService()
    {
        var services = new ServiceCollection();

        services.AddSecFinancialFactsWorker();

        var hostedServiceDescriptors = services
            .Where(d => d.ServiceType == typeof(IHostedService))
            .ToList();

        hostedServiceDescriptors
            .Should()
            .Contain(
                d => d.ImplementationType == typeof(FinancialFactsScraperWorker),
                "AddHostedService<FinancialFactsScraperWorker>() must register the worker as IHostedService so the SEC Company Facts walker runs at startup"
            );
    }
}
