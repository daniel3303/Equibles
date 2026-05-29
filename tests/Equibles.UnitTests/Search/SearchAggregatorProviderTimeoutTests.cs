using System;
using System.Threading;
using System.Threading.Tasks;
using Equibles.Search;
using Equibles.Search.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Equibles.UnitTests.Search;

public class SearchAggregatorProviderTimeoutTests
{
    private static SearchAggregator Build(params ISearchProvider[] providers)
    {
        var services = new ServiceCollection();
        foreach (var provider in providers)
        {
            services.AddScoped(typeof(ISearchProvider), _ => provider);
        }
        return new SearchAggregator(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            NullLogger<SearchAggregator>.Instance
        );
    }

    [Fact]
    public async Task Search_ProviderIgnoresTokenAndStalls_GroupOmittedHealthyStillReturned()
    {
        // Contract (XML doc): "A provider that ... exceeds ProviderTimeout is logged and
        // dropped; one slow or broken module never breaks the results page." A provider
        // that ignores the cancellation token must still be backstopped by the timeout,
        // so the healthy group renders and the page does not stall on the slow one.
        var aggregator = Build(new StallingProvider("Slow", 0), new HealthyProvider("Healthy", 1));

        var result = await aggregator.Search("query", 5, CancellationToken.None);

        result.Should().ContainSingle();
        result[0].Category.Should().Be("Healthy");
    }

    // Ignores the cancellation token entirely and completes well past the 5s
    // ProviderTimeout — the exact "ignores the token" case the backstop guards.
    private sealed class StallingProvider : ISearchProvider
    {
        public StallingProvider(string category, int order)
        {
            Category = category;
            Order = order;
        }

        public string Category { get; }

        public int Order { get; }

        public async Task<SearchResultGroup> Search(
            SearchRequest request,
            CancellationToken cancellationToken
        )
        {
            await Task.Delay(TimeSpan.FromSeconds(20));
            return new SearchResultGroup
            {
                Category = Category,
                Order = Order,
                Hits = [new SearchHit { Title = "late" }],
            };
        }
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
