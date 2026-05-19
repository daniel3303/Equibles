using System;
using System.Threading;
using System.Threading.Tasks;
using Equibles.Search;
using Equibles.Search.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Equibles.UnitTests.Search;

public class SearchAggregatorNullGroupTests
{
    private static SearchAggregator Build(params ISearchProvider[] providers)
    {
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

    [Fact]
    public async Task Search_ProviderReturnsNullGroup_IsDroppedAndHealthyGroupStillReturned()
    {
        // Contract (XML doc + the "RunProvider never returns null" / Empty-sentinel
        // comment): a degraded provider must not NRE the ordering step and must be
        // dropped uniformly, while healthy providers still return. A provider whose
        // Search completes with a null SearchResultGroup is exactly such a provider.
        var aggregator = Build(
            new NullGroupProvider("Broken", 0),
            new HealthyProvider("Healthy", 1)
        );

        var result = await aggregator.Search("query", 5, CancellationToken.None);

        result.Should().ContainSingle();
        result[0].Category.Should().Be("Healthy");
    }

    private sealed class NullGroupProvider : ISearchProvider
    {
        public NullGroupProvider(string category, int order)
        {
            Category = category;
            Order = order;
        }

        public string Category { get; }

        public int Order { get; }

        public Task<SearchResultGroup> Search(
            SearchRequest request,
            CancellationToken cancellationToken
        ) => Task.FromResult<SearchResultGroup>(null);
    }

    private sealed class HealthyProvider : ISearchProvider
    {
        public HealthyProvider(string category, int order)
        {
            Category = category;
            Order = order;
        }

        public string Category { get; }

        public int Order { get; }

        public Task<SearchResultGroup> Search(
            SearchRequest request,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult(
                new SearchResultGroup
                {
                    Category = Category,
                    Order = Order,
                    Hits = [new SearchHit { Title = "hit" }],
                }
            );
    }
}
