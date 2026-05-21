using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Equibles.Search;
using Equibles.Search.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Equibles.UnitTests.Search;

public class SearchAggregatorSanitizeForLogEscTests
{
    [Fact]
    public async Task Search_ProviderThrows_LogsQueryWithAnsiEscapeStripped()
    {
        // Sibling pin to Search_ProviderThrows_LogsQueryWithControlCharactersStripped.
        // The existing pin only exercises `\r`, `\n`, and `\t` — the three "obvious"
        // log-forging characters. The production guard at SearchAggregator.SanitizeForLog
        // uses `char.IsControl(c)`, which covers the full 0x00–0x1F + 0x7F + other
        // Unicode control ranges. The most dangerous arm of that predicate from a
        // log-injection standpoint is the ANSI ESC byte (`\x1B`, 0x1B) — terminal-
        // rendered logs (the project's Serilog console sink + `docker logs`) interpret
        // `\x1B[<n>m` as a colour/cursor escape and a crafted query like
        // `\x1B[2J\x1B[H` clears the screen and homes the cursor, hiding subsequent
        // log lines from an operator triaging an incident. CR/LF only forge new lines;
        // ESC actively rewrites the rendered display.
        //
        // The risk: a refactor that "specialises" the predicate to a fast-path
        // `c is '\r' or '\n' or '\t'` (under the false intuition that the existing
        // CR/LF/TAB pin enumerates the full intent of the sanitiser) would compile,
        // pass the existing pin, and leak ESC into the rendered logs. Pin the ESC
        // arm so the regression surfaces here.
        var logger = new CapturingLogger<SearchAggregator>();
        var aggregator = Build(
            logger,
            new ThrowingProvider("Broken", 0, new InvalidOperationException("boom"))
        );

        await aggregator.Search("safe\x1B[31mevil\x1B[0m", 5, CancellationToken.None);

        logger.Messages.Should().ContainSingle();
        var message = logger.Messages[0];
        message.Should().Contain("safe");
        message.Should().Contain("evil");
        message.Should().NotContain("\x1B");
    }

    private static SearchAggregator Build(
        ILogger<SearchAggregator> logger,
        params ISearchProvider[] providers
    )
    {
        var services = new ServiceCollection();
        foreach (var provider in providers)
        {
            services.AddScoped(typeof(ISearchProvider), _ => provider);
        }
        var serviceProvider = services.BuildServiceProvider();

        return new SearchAggregator(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            logger
        );
    }

    private sealed class ThrowingProvider : ISearchProvider
    {
        private readonly Exception _toThrow;

        public ThrowingProvider(string category, int order, Exception toThrow)
        {
            Category = category;
            Order = order;
            _toThrow = toThrow;
        }

        public string Category { get; }

        public int Order { get; }

        public Task<SearchResultGroup> Search(
            SearchRequest request,
            CancellationToken cancellationToken
        ) => throw _toThrow;
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = new();

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception exception,
            Func<TState, Exception, string> formatter
        )
        {
            Messages.Add(formatter(state, exception));
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose() { }
        }
    }
}
