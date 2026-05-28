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

/// <summary>
/// Adversarial Lane A. <c>BuildSecondaryCikToParent</c>'s warning template
/// uses the named placeholder <c>{ExistingParent}</c> twice — first inside
/// the parents-conflict parenthetical, then again in "keeping
/// {ExistingParent}." A reader of that log must see the existing parent's
/// ticker at BOTH sites for the message to be useful: the second
/// occurrence is the one that names which parent will win after manual
/// cleanup. A regression that left an occurrence unsubstituted (the
/// classic CA2017 placeholder/argument count mismatch) would emit a
/// message like "keeping {ExistingParent}. Manual cleanup required." —
/// unreadable to whoever's triaging the conflict.
/// </summary>
public class CompanySyncServiceBuildSecondaryCikToParentTests
{
    [Fact]
    public void BuildSecondaryCikToParent_DuplicateSubsidiaryCik_WarningRendersExistingTickerAtEveryPlaceholderSite()
    {
        var capturing = new CapturingLogger();
        var sut = new CompanySyncService(
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ISecEdgarClient>(),
            Options.Create(new WorkerOptions()),
            capturing,
            new ErrorReporter(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            )
        );

        var firstParent = new CommonStock
        {
            Cik = "0000001000",
            Ticker = "AAA",
            Name = "Apple",
            SecondaryCiks = ["0000005555"],
        };
        var duplicateParent = new CommonStock
        {
            Cik = "0000002000",
            Ticker = "BBB",
            Name = "BeeCo",
            SecondaryCiks = ["0000005555"],
        };

        var method = typeof(CompanySyncService).GetMethod(
            "BuildSecondaryCikToParent",
            BindingFlags.NonPublic | BindingFlags.Instance
        );
        method.Invoke(sut, [new List<CommonStock> { firstParent, duplicateParent }]);

        var warning = capturing.Warnings.Should().ContainSingle().Subject;
        warning
            .Should()
            .NotContain(
                "{ExistingParent}",
                "an unsubstituted placeholder means the reader can't tell which parent the system kept"
            );
        warning.Should().Contain("AAA", "the existing parent's ticker must appear in the message");
        warning.Should().Contain("BBB", "the duplicate parent's ticker must appear in the message");
    }

    private sealed class CapturingLogger : ILogger<CompanySyncService>
    {
        public List<string> Warnings { get; } = new();

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception exception,
            Func<TState, Exception, string> formatter
        )
        {
            if (logLevel == LogLevel.Warning)
                Warnings.Add(formatter(state, exception));
        }
    }
}
