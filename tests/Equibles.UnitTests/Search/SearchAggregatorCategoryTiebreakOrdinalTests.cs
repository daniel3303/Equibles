using Equibles.Search;
using Equibles.Search.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Equibles.UnitTests.Search;

public class SearchAggregatorCategoryTiebreakOrdinalTests
{
    // The existing SearchAggregatorTests cover the primary `OrderBy(Order)`
    // arm — all three providers there use distinct Order values, so the
    // secondary `.ThenBy(group => group.Category ?? string.Empty,
    // StringComparer.Ordinal)` is NEVER exercised. With seven shipped
    // providers spaced ten apart (Stocks=0 ... Futures=60) the tie-break
    // is unreachable in production today, but the moment a maintainer
    // adds two providers in the same module (e.g. two SEC sub-groups
    // both ordered 10) the comparer becomes user-visible.
    //
    // Adversarial input: two providers with identical Order, categories
    // "alpha" and "Beta". The Ordinal comparer makes "Beta" (0x42) sort
    // BEFORE "alpha" (0x61) — uppercase ASCII precedes lowercase. A
    // refactor swapping in StringComparer.OrdinalIgnoreCase (a plausible
    // "consistency" change matching the Hit-title sort on line 84) would
    // flip the order to "alpha" then "Beta" and compile silently. The
    // CultureInfo.CurrentCulture default would do the same thing AND
    // introduce locale flakiness on CI runners with non-invariant
    // cultures (e.g. tr-TR's dotless-i collision). Pinning the case-
    // sensitive ordinal arm defends both regressions at once.
    [Fact]
    public async Task Search_TwoProvidersAtSameOrder_TiebreaksByCategoryOrdinalCaseSensitive()
    {
        var aggregator = Build(
            new ProviderLowercase("alpha", 0, _ => Group("alpha", 0, 1)),
            new ProviderUppercase("Beta", 0, _ => Group("Beta", 0, 1))
        );

        var result = await aggregator.Search("are", 5, CancellationToken.None);

        result.Select(g => g.Category).Should().Equal("Beta", "alpha");
    }

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

        public Task<SearchResultGroup> Search(
            SearchRequest request,
            CancellationToken cancellationToken
        ) => Task.FromResult(_factory(request));
    }

    private sealed class ProviderLowercase : StubProvider
    {
        public ProviderLowercase(
            string category,
            int order,
            Func<SearchRequest, SearchResultGroup> factory
        )
            : base(category, order, factory) { }
    }

    private sealed class ProviderUppercase : StubProvider
    {
        public ProviderUppercase(
            string category,
            int order,
            Func<SearchRequest, SearchResultGroup> factory
        )
            : base(category, order, factory) { }
    }
}
