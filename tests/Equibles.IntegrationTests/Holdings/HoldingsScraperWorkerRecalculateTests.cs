using System.Reflection;
using Equibles.Core.Configuration;
using Equibles.Errors.BusinessLogic;
using Equibles.Holdings.HostedService;
using Equibles.IntegrationTests.Helpers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.IntegrationTests.Holdings;

/// <summary>
/// Pins the resilience catch in <c>RecalculatePendingValues</c>: when the
/// <c>HoldingsValueRecalculator</c> resolution or call throws, the worker must
/// log + swallow so the daily cycle still terminates. A regression that promoted
/// the failure to a re-throw would crash the worker every cycle the
/// recalculator path is unhealthy — and the recalculator pipeline depends on
/// Yahoo prices which routinely go missing.
/// </summary>
public class HoldingsScraperWorkerRecalculateTests
{
    [Fact]
    public async Task RecalculatePendingValues_RecalculatorResolutionFails_LogsAndDoesNotThrow()
    {
        // Empty scope factory — GetRequiredService<HoldingsValueRecalculator> throws
        // InvalidOperationException. The catch (Exception) clause must swallow it.
        var scopeFactory = ServiceScopeSubstitute.Create();
        var configuration = Substitute.For<IConfiguration>();
        configuration["Sec:ContactEmail"].Returns("test@example.com");

        var worker = new HoldingsScraperWorker(
            Substitute.For<ILogger<HoldingsScraperWorker>>(),
            scopeFactory,
            new ErrorReporter(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            ),
            Options.Create(new WorkerOptions()),
            configuration,
            new HoldingsRescanSignal()
        );

        var method = typeof(HoldingsScraperWorker).GetMethod(
            "RecalculatePendingValues",
            BindingFlags.NonPublic | BindingFlags.Instance
        );

        var act = async () => await (Task)method.Invoke(worker, [CancellationToken.None]);

        // Reflection wraps user exceptions in TargetInvocationException, but the
        // catch in the production method should swallow this entirely — assert no
        // throw at all so a regression where the catch is narrowed/removed surfaces.
        await act.Should().NotThrowAsync();
    }
}
