using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Equibles.Search;
using Equibles.Search.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Equibles.UnitTests.Search;

public class SearchAggregatorTests
{
    private static SearchAggregator Build(params ISearchProvider[] providers)
    {
        return Build(NullLogger<SearchAggregator>.Instance, providers);
    }

    private static SearchAggregator Build(
        ILogger<SearchAggregator> logger,
        params ISearchProvider[] providers
    )
    {
        // Register under ISearchProvider so the aggregator's per-scope, by-concrete-type
        // resolution is exercised as in production. Each stub is a distinct sealed type
        // because production providers are distinct classes and the aggregator dedups by type.
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

    private static SearchResultGroup Group(string category, int order, int hits)
    {
        return new SearchResultGroup
        {
            Category = category,
            Order = order,
            Hits = Enumerable
                .Range(0, hits)
                .Select(i => new SearchHit { Title = $"{category}-{i}" })
                .ToList(),
        };
    }

    [Fact]
    public async Task Search_OrdersByOrderThenCategory_AndDropsEmptyGroups()
    {
        var aggregator = Build(
            new StubA("Beta", 10, _ => Group("Beta", 10, 1)),
            new StubB("Alpha", 0, _ => Group("Alpha", 0, 2)),
            new StubC("Empty", 5, _ => Group("Empty", 5, 0))
        );

        var result = await aggregator.Search("are", 5, CancellationToken.None);

        result.Select(g => g.Category).Should().Equal("Alpha", "Beta");
    }

    private static SearchResultGroup GroupWithTitles(string category, params string[] titles)
    {
        return new SearchResultGroup
        {
            Category = category,
            Order = 0,
            Hits = titles.Select(t => new SearchHit { Title = t }).ToList(),
        };
    }

    [Fact]
    public async Task Search_SortByName_OrdersHitsByTitleCaseInsensitive()
    {
        var aggregator = Build(
            new StubA(
                "Stocks",
                0,
                _ => GroupWithTitles("Stocks", "delta", "Alpha", "charlie", "Bravo")
            )
        );

        var result = await aggregator.Search("are", 5, CancellationToken.None, SearchSort.Name);

        result.Should().ContainSingle();
        result[0].Hits.Select(h => h.Title).Should().Equal("Alpha", "Bravo", "charlie", "delta");
    }

    [Fact]
    public async Task Search_SortByRelevance_PreservesProviderHitOrder()
    {
        var aggregator = Build(
            new StubA("Stocks", 0, _ => GroupWithTitles("Stocks", "zeta", "alpha", "mid"))
        );

        var result = await aggregator.Search(
            "are",
            5,
            CancellationToken.None,
            SearchSort.Relevance
        );

        result.Should().ContainSingle();
        result[0].Hits.Select(h => h.Title).Should().Equal("zeta", "alpha", "mid");
    }

    [Fact]
    public async Task Search_DefaultSort_PreservesProviderHitOrder()
    {
        var aggregator = Build(
            new StubA("Stocks", 0, _ => GroupWithTitles("Stocks", "zeta", "alpha", "mid"))
        );

        var result = await aggregator.Search("are", 5, CancellationToken.None);

        result[0].Hits.Select(h => h.Title).Should().Equal("zeta", "alpha", "mid");
    }

    [Fact]
    public async Task Search_DateRange_IsThreadedIntoTheProviderRequest()
    {
        SearchRequest captured = null;
        var from = new DateOnly(2024, 1, 1);
        var to = new DateOnly(2024, 6, 30);
        var aggregator = Build(
            new StubA(
                "Stocks",
                0,
                request =>
                {
                    captured = request;
                    return GroupWithTitles("Stocks", "AAPL");
                }
            )
        );

        await aggregator.Search("are", 5, CancellationToken.None, SearchSort.Relevance, from, to);

        captured.Should().NotBeNull();
        captured.DateFrom.Should().Be(from);
        captured.DateTo.Should().Be(to);
    }

    [Fact]
    public async Task Search_NoDateRange_LeavesProviderRequestBoundsNull()
    {
        SearchRequest captured = null;
        var aggregator = Build(
            new StubA(
                "Stocks",
                0,
                request =>
                {
                    captured = request;
                    return GroupWithTitles("Stocks", "AAPL");
                }
            )
        );

        await aggregator.Search("are", 5, CancellationToken.None);

        captured.DateFrom.Should().BeNull();
        captured.DateTo.Should().BeNull();
    }

    [Fact]
    public async Task Search_OneProviderThrows_OtherGroupsStillReturned()
    {
        var aggregator = Build(
            new StubA("Broken", 0, _ => throw new InvalidOperationException("provider blew up")),
            new StubB("Healthy", 1, _ => Group("Healthy", 1, 1))
        );

        var result = await aggregator.Search("are", 5, CancellationToken.None);

        result.Should().ContainSingle();
        result[0].Category.Should().Be("Healthy");
    }

    [Fact]
    public async Task Search_ProviderObservesCancellation_IsIsolatedNotPropagated()
    {
        var aggregator = Build(
            new StubA(
                "Slow",
                0,
                _ => throw new OperationCanceledException("provider hit its timeout")
            ),
            new StubB("Healthy", 1, _ => Group("Healthy", 1, 1))
        );

        var result = await aggregator.Search("are", 5, CancellationToken.None);

        result.Should().ContainSingle();
        result[0].Category.Should().Be("Healthy");
    }

    [Fact]
    public async Task Search_BlankQuery_ReturnsEmptyAndDoesNotInvokeProviders()
    {
        var stub = new StubA("Stocks", 0, _ => Group("Stocks", 0, 1));
        var aggregator = Build(stub);

        var result = await aggregator.Search("   ", 5, CancellationToken.None);

        result.Should().BeEmpty();
        stub.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task Search_ProviderThrows_LogsQueryWithControlCharactersStripped()
    {
        // A query carrying CR/LF/TAB must not reach the rendered log verbatim — otherwise a
        // crafted search term could forge log lines (CodeQL cs/log-forging, alert #351).
        var logger = new CapturingLogger<SearchAggregator>();
        var aggregator = Build(
            logger,
            new StubA("Broken", 0, _ => throw new InvalidOperationException("boom"))
        );

        await aggregator.Search("ab\r\ncd\tef", 5, CancellationToken.None);

        logger.Messages.Should().ContainSingle();
        var message = logger.Messages[0];
        message.Should().Contain("abcdef");
        message.Should().NotContainAny("\r", "\n", "\t");
    }

    // Minimal ILogger that records the fully-formatted message; the project only references
    // Logging.Abstractions, so we avoid pulling in a fake-logger package for one assertion.
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

    private abstract class StubProvider : ISearchProvider
    {
        private readonly Func<SearchRequest, SearchResultGroup> _factory;

        protected StubProvider(
            string category,
            int order,
            Func<SearchRequest, SearchResultGroup> factory
        )
        {
            Category = category;
            Order = order;
            _factory = factory;
        }

        public string Category { get; }

        public int Order { get; }

        public int CallCount { get; private set; }

        public Task<SearchResultGroup> Search(
            SearchRequest request,
            CancellationToken cancellationToken
        )
        {
            CallCount++;
            return Task.FromResult(_factory(request));
        }
    }

    private sealed class StubA : StubProvider
    {
        public StubA(string category, int order, Func<SearchRequest, SearchResultGroup> factory)
            : base(category, order, factory) { }
    }

    private sealed class StubB : StubProvider
    {
        public StubB(string category, int order, Func<SearchRequest, SearchResultGroup> factory)
            : base(category, order, factory) { }
    }

    private sealed class StubC : StubProvider
    {
        public StubC(string category, int order, Func<SearchRequest, SearchResultGroup> factory)
            : base(category, order, factory) { }
    }
}
