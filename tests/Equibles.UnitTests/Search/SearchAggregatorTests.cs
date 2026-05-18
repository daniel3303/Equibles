using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Equibles.Search;
using Equibles.Search.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Equibles.UnitTests.Search;

public class SearchAggregatorTests
{
    private static SearchAggregator Build(params ISearchProvider[] providers)
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
            NullLogger<SearchAggregator>.Instance
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
