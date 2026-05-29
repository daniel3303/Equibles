using System.Reflection;
using Equibles.Core.Configuration;
using Equibles.Errors.BusinessLogic;
using Equibles.Sec.HostedService;
using Equibles.Sec.HostedService.Configuration;
using Equibles.Sec.HostedService.Contracts;
using Equibles.Sec.HostedService.Models;
using Equibles.Sec.HostedService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Polly;
using Xunit;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Adversarial pin on <c>DocumentScraper.BuildRetryPipeline</c>'s OnRetry handler.
/// Contract (the handler's own stated intent): when the pipeline exhausts every
/// attempt, the final failure is logged at Error level — the
/// "Document creation failed after {AttemptNumber} attempts" branch. Earlier
/// attempts log Warning. Derived from the handler's intent before reading the
/// branch condition.
/// </summary>
public class DocumentScraperBuildRetryPipelineFinalAttemptErrorLogTests
{
    [Fact(Timeout = 60000)]
    public async Task BuildRetryPipeline_AfterExhaustingAllRetries_LogsErrorOnFinalAttempt()
    {
        var logger = Substitute.For<ILogger<DocumentScraper>>();
        var scraper = new DocumentScraper(
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ICompanySyncService>(),
            new List<IFilingProcessor>(),
            Options.Create(new DocumentScraperOptions()),
            Options.Create(new WorkerOptions()),
            logger,
            new ErrorReporter(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            )
        );

        var pipeline = (ResiliencePipeline)
            typeof(DocumentScraper)
                .GetMethod("BuildRetryPipeline", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(scraper, [])!;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(50));
        var act = async () =>
            await pipeline.ExecuteAsync(
                async (CancellationToken _) =>
                {
                    await Task.Yield();
                    throw new InvalidOperationException("always fails");
                },
                cts.Token
            );

        // The operation must still surface its exception after all retries.
        await act.Should().ThrowAsync<InvalidOperationException>();

        var logCalls = logger
            .ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(ILogger.Log))
            .Select(c => (LogLevel)c.GetArguments()[0]!)
            .ToList();
        var warnings = logCalls.Count(l => l == LogLevel.Warning);
        var errors = logCalls.Count(l => l == LogLevel.Error);

        errors
            .Should()
            .BeGreaterThan(
                0,
                "exhausting all retries should log the failure at Error level, "
                    + $"but only {warnings} Warning and {errors} Error logs were emitted"
            );
    }
}
