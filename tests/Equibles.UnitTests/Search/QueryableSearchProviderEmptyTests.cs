using Equibles.Search.Abstractions;

namespace Equibles.UnitTests.Search;

public class QueryableSearchProviderEmptyTests
{
    // Sibling to QueryableSearchProviderTests.Search_FilterYieldsMoreThanMaxPerProvider
    // (the cap-to-N pin). The empty-result arm — Filter yields zero matches — is
    // unpinned. SearchAggregator filters out empty groups in the final assembly
    // step (`.Where(group => group.Hits.Count > 0)`), so an empty result is the
    // normal, expected outcome for any provider whose module data hasn't yet
    // surfaced a match for the query.
    //
    // The risks this pin uniquely catches:
    //   • A refactor that returns `null` instead of empty `Hits` on no-match
    //     would NRE the aggregator's `.Where(group => group.Hits.Count > 0)`
    //     filter — every search request would 500 on a per-provider empty.
    //   • A "defensive" refactor that defaults to a single placeholder hit
    //     when Filter is empty (e.g. "no results for '{query}'" in the
    //     provider) would compile and pass the cap-to-N sibling (which
    //     supplies a non-empty filter), and silently produce a phantom
    //     entry in every empty-result group.
    //   • Category / Order metadata must still be populated even on an
    //     empty result, so the aggregator's grouping pipeline doesn't
    //     fall back to defaults.
    //
    // Pin: a Filter that yields zero items → Hits is empty (not null), and
    // Category / Order are still propagated from the provider's properties.
    [Fact]
    public async Task Search_FilterYieldsNoMatches_ReturnsEmptyHitsAndPopulatesCategoryAndOrder()
    {
        var provider = new StubProvider([]);
        var request = new SearchRequest { Query = "no-such-thing", MaxPerProvider = 10 };

        var group = await provider.Search(request, CancellationToken.None);

        group.Should().NotBeNull();
        group.Category.Should().Be("Stub");
        group.Order.Should().Be(7);
        group.Hits.Should().NotBeNull();
        group.Hits.Should().BeEmpty();
    }

    private sealed class StubProvider : QueryableSearchProvider<string>
    {
        private readonly List<string> _items;

        public StubProvider(List<string> items) => _items = items;

        public override string Category => "Stub";

        public override int Order => 7;

        protected override IQueryable<string> Filter(SearchRequest request) => _items.AsQueryable();

        protected override Task<List<string>> Materialize(
            IQueryable<string> query,
            CancellationToken cancellationToken
        ) => Task.FromResult(query.ToList());

        protected override SearchHit Project(string entity) => new() { Title = entity };
    }
}
