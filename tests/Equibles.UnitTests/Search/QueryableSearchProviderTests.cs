using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Equibles.Search.Abstractions;

namespace Equibles.UnitTests.Search;

public class QueryableSearchProviderTests
{
    [Fact]
    public async Task Search_FilterYieldsMoreThanMaxPerProvider_CapsToFirstNInFilterOrder()
    {
        // Contract (XML doc): the base pipeline caps to SearchRequest.MaxPerProvider
        // against the "(ideally ordered) query ... before the result cap". So with an
        // ordered Filter of 5 and MaxPerProvider 2, exactly the first 2 must survive.
        var provider = new StubProvider(["a", "b", "c", "d", "e"]);
        var request = new SearchRequest { Query = "x", MaxPerProvider = 2 };

        var group = await provider.Search(request, CancellationToken.None);

        group.Category.Should().Be("Stub");
        group.Order.Should().Be(7);
        group.Hits.Select(h => h.Title).Should().Equal("a", "b");
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
