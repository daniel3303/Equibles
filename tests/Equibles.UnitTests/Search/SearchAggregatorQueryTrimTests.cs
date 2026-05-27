using Equibles.Search;
using Equibles.Search.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Equibles.UnitTests.Search;

public class SearchAggregatorQueryTrimTests
{
    [Fact]
    public async Task Search_QueryWithSurroundingWhitespace_PassesTrimmedQueryToProvider()
    {
        // SearchAggregator.Search builds the per-provider request with
        // `Query = query.Trim()` (SearchAggregator.cs:56). The blank-query
        // pin asserts whitespace-only short-circuits to []; the DateRange pin
        // captures the request to assert date-bound threading. Neither
        // exercises the Trim — a refactor dropping `.Trim()` ("the upstream
        // controller already trims") would compile, pass every existing pin,
        // and silently feed "  AAPL  " to provider repositories whose SQL
        // `LIKE` patterns would never match — the user types "AAPL" but the
        // search returns empty. Pin: capture the SearchRequest and assert
        // the provider sees the un-padded query string.
        SearchRequest captured = null;
        var services = new ServiceCollection();
        services.AddScoped<ISearchProvider>(_ => new CaptureProvider(r => captured = r));
        var serviceProvider = services.BuildServiceProvider();
        var aggregator = new SearchAggregator(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<SearchAggregator>.Instance
        );

        await aggregator.Search("  AAPL  ", 5, CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.Query.Should().Be("AAPL");
    }

    private sealed class CaptureProvider : ISearchProvider
    {
        private readonly Action<SearchRequest> _capture;

        public CaptureProvider(Action<SearchRequest> capture) => _capture = capture;

        public string Category => "Test";

        public int Order => 0;

        public Task<SearchResultGroup> Search(
            SearchRequest request,
            CancellationToken cancellationToken
        )
        {
            _capture(request);
            return Task.FromResult(
                new SearchResultGroup
                {
                    Category = Category,
                    Order = Order,
                    Hits = [new SearchHit { Title = "hit" }],
                }
            );
        }
    }
}
