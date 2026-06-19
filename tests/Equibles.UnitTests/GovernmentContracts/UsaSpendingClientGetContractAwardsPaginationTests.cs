using System.Net;
using Equibles.Integrations.GovernmentContracts;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.UnitTests.GovernmentContracts;

/// <summary>
/// Pins <see cref="UsaSpendingClient.GetContractAwards"/>'s pagination contract: it must
/// walk every page, accumulating records in page order, and stop the moment the API
/// reports no next page. A regression that dropped earlier pages, stopped after the first,
/// or kept paging past <c>hasNext: false</c> would silently corrupt the contract feed.
/// </summary>
public class UsaSpendingClientGetContractAwardsPaginationTests
{
    [Fact]
    public async Task GetContractAwards_AccumulatesAllPagesInOrder_AndStopsWhenHasNextIsFalse()
    {
        var page1 =
            "{\"results\":["
            + "{\"generated_internal_id\":\"CONT_AWD_1\",\"Award ID\":\"PIID-1\"},"
            + "{\"generated_internal_id\":\"CONT_AWD_2\",\"Award ID\":\"PIID-2\"}],"
            + "\"page_metadata\":{\"page\":1,\"hasNext\":true}}";
        var page2 =
            "{\"results\":["
            + "{\"generated_internal_id\":\"CONT_AWD_3\",\"Award ID\":\"PIID-3\"},"
            + "{\"generated_internal_id\":\"CONT_AWD_4\",\"Award ID\":\"PIID-4\"}],"
            + "\"page_metadata\":{\"page\":2,\"hasNext\":false}}";
        var handler = new PagedHandler(page1, page2);
        var sut = new UsaSpendingClient(
            new HttpClient(handler),
            Substitute.For<ILogger<UsaSpendingClient>>()
        );

        var awards = await sut.GetContractAwards(
            new DateOnly(2024, 1, 1),
            new DateOnly(2024, 12, 31),
            minimumAmount: 0m
        );

        // Both pages flattened in order, and the second page's hasNext:false halted
        // paging — a third request would mean the stop condition was missed.
        awards
            .Select(a => a.GeneratedInternalId)
            .Should()
            .Equal("CONT_AWD_1", "CONT_AWD_2", "CONT_AWD_3", "CONT_AWD_4");
        handler.CallCount.Should().Be(2);
    }

    private sealed class PagedHandler : HttpMessageHandler
    {
        private readonly string[] _pages;
        public int CallCount { get; private set; }

        public PagedHandler(params string[] pages) => _pages = pages;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            var body = _pages[Math.Min(CallCount, _pages.Length - 1)];
            CallCount++;
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) }
            );
        }
    }
}
