using System.Net;
using Equibles.Integrations.GovernmentContracts;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NSubstitute;

namespace Equibles.UnitTests.GovernmentContracts;

/// <summary>
/// Pins the amount-descending cursor that replaced deep offset pagination in
/// <see cref="UsaSpendingClient.GetContractAwards"/>. USAspending's search endpoint
/// degrades sharply with page depth and drops the connection outright around page 90
/// ("The response ended prematurely" — the failure that froze the production backfill
/// on a 118k-record week), so the client must never page deep: after its shallow page
/// budget it restarts from page 1 with the band's upper amount bound tightened to the
/// smallest amount fetched. These tests drive the client against a fake handler that
/// honors the amount band and page number of each request, so they verify completeness
/// (every award fetched exactly once) as well as the shallow-page invariant.
/// </summary>
public class UsaSpendingClientAmountCursorTests
{
    // Mirrors the client's constants: the shallow budget is 10 pages, the hard
    // per-band ceiling (the API's own 10k-record limit) is 100 pages.
    private const int MaxPagesPerBand = 10;
    private const int MaxPages = 100;

    [Fact]
    public async Task GetContractAwards_WindowDenserThanPageBudget_TightensUpperBoundInsteadOfPagingDeep()
    {
        // 130 awards with strictly-descending amounts, served 5 per page: 10 pages cover
        // 50 awards, so a naive pager would need 26 pages. The cursor must instead reset
        // the band (upper bound = smallest amount seen, back to page 1) and still return
        // every award exactly once, with no request ever exceeding the shallow budget.
        var awards = Enumerable
            .Range(0, 130)
            .Select(i => new FakeAward($"CONT_AWD_{i}", 1_000_000m + (130 - i) * 1000m))
            .ToList();
        var handler = new BandAwareHandler(awards, pageSize: 5);
        var sut = new UsaSpendingClient(
            new HttpClient(handler),
            Substitute.For<ILogger<UsaSpendingClient>>()
        );

        var result = await sut.GetContractAwards(
            new DateOnly(2022, 1, 16),
            new DateOnly(2022, 1, 22),
            minimumAmount: 1_000_000m
        );

        result
            .Select(r => r.GeneratedInternalId)
            .Should()
            .BeEquivalentTo(
                awards.Select(a => a.Id),
                "the cursor must return every award exactly once — no page skipped by a band reset, no boundary tie duplicated"
            );
        handler.MaxPageRequested.Should().BeLessThanOrEqualTo(MaxPagesPerBand);
        handler
            .UpperBoundsSeen.Should()
            .BeInDescendingOrder("each band reset must tighten the upper bound monotonically");
    }

    [Fact]
    public async Task GetContractAwards_TieRunLongerThanPageBudget_PagesThroughTheTiesThenResetsTheBand()
    {
        // Awards 20..89 all share one amount — a 70-row tie run at 6 rows/page. The first
        // band reset lands the cursor on a band whose top 10 pages (60 rows) are ALL that
        // tie amount: the smallest amount on the page equals the band's upper bound, so
        // the cursor CANNOT tighten (an equal bound would refetch the same pages forever).
        // It must page on through the ties — the one sanctioned use of deeper pages — and
        // reset only once the amount drops.
        var awards = new List<FakeAward>();
        for (var i = 0; i < 20; i++)
            awards.Add(new FakeAward($"HIGH_{i}", 9_000_000m - i * 1000m));
        for (var i = 0; i < 70; i++)
            awards.Add(new FakeAward($"TIE_{i}", 5_000_000m));
        for (var i = 0; i < 20; i++)
            awards.Add(new FakeAward($"LOW_{i}", 4_000_000m - i * 1000m));

        var handler = new BandAwareHandler(awards, pageSize: 6);
        var sut = new UsaSpendingClient(
            new HttpClient(handler),
            Substitute.For<ILogger<UsaSpendingClient>>()
        );

        var result = await sut.GetContractAwards(
            new DateOnly(2022, 1, 16),
            new DateOnly(2022, 1, 22),
            minimumAmount: 1_000_000m
        );

        result
            .Select(r => r.GeneratedInternalId)
            .Should()
            .BeEquivalentTo(
                awards.Select(a => a.Id),
                "ties at the band boundary must be re-covered by the inclusive upper bound and deduplicated, never lost"
            );
        handler
            .MaxPageRequested.Should()
            .BeGreaterThan(
                MaxPagesPerBand,
                "the tie run can only be crossed by paging past the shallow budget"
            );
        handler
            .MaxPageRequested.Should()
            .BeLessThanOrEqualTo(MaxPages, "the API refuses pages past the 10,000th record");
    }

    [Fact]
    public async Task GetContractAwards_TieRunExceedingTheApiCeiling_BisectsTheDateWindow()
    {
        // Every page carries the same amount and hasNext stays true for the full window —
        // a tie run past the API's 100-page ceiling. The client can't advance the amount
        // cursor, so it must split the date range and rescan the halves (which the handler
        // reports as completed) instead of looping forever or silently giving up.
        var handler = new EndlessTieHandler(
            fullWindowStart: "2022-01-16",
            fullWindowEnd: "2022-01-22"
        );
        var sut = new UsaSpendingClient(
            new HttpClient(handler),
            Substitute.For<ILogger<UsaSpendingClient>>()
        );

        await sut.GetContractAwards(
            new DateOnly(2022, 1, 16),
            new DateOnly(2022, 1, 22),
            minimumAmount: 1_000_000m
        );

        handler
            .WindowsSeen.Should()
            .Contain(
                new[] { ("2022-01-16", "2022-01-19"), ("2022-01-20", "2022-01-22") },
                "an unpageable tie run must bisect the window so the ties land in smaller date ranges"
            );
    }

    [Fact]
    public async Task GetContractAwards_CentValuedAmounts_SendsWholeDollarBoundsAndLosesNothing()
    {
        // Real award amounts carry cents (the first production cycle died on
        // upper_bound=532543287.51 — the API 422s any fractional bound). The cursor
        // must round its bound UP to a whole dollar; the inclusive band then
        // re-covers everything between the true floor and the ceiling, deduplicated
        // by id, so completeness still holds.
        var awards = Enumerable
            .Range(0, 130)
            .Select(i => new FakeAward($"CENTS_{i}", 1_000_000m + (130 - i) * 997.13m))
            .ToList();
        var handler = new BandAwareHandler(awards, pageSize: 5);
        var sut = new UsaSpendingClient(
            new HttpClient(handler),
            Substitute.For<ILogger<UsaSpendingClient>>()
        );

        var result = await sut.GetContractAwards(
            new DateOnly(2022, 1, 2),
            new DateOnly(2022, 1, 8),
            minimumAmount: 1_000_000m
        );

        result
            .Select(r => r.GeneratedInternalId)
            .Should()
            .BeEquivalentTo(
                awards.Select(a => a.Id),
                "rounding the bound up must widen the band, never narrow it — no award may be lost"
            );
        handler
            .UpperBoundsSeen.Should()
            .OnlyContain(
                u => decimal.Ceiling(u) == u,
                "the API rejects fractional amount bounds with a 422"
            );
        handler.MaxPageRequested.Should().BeLessThanOrEqualTo(MaxPagesPerBand);
    }

    private sealed record FakeAward(string Id, decimal Amount);

    /// <summary>
    /// Serves a fixed amount-descending dataset the way the real endpoint does: honors the
    /// request's award_amounts band (both bounds inclusive, as verified against the live
    /// API) and its page number, and records the deepest page and the bands requested.
    /// </summary>
    private sealed class BandAwareHandler : HttpMessageHandler
    {
        private readonly List<FakeAward> _sorted;
        private readonly int _pageSize;

        public int MaxPageRequested { get; private set; }
        public List<decimal> UpperBoundsSeen { get; } = [];

        public BandAwareHandler(IEnumerable<FakeAward> awards, int pageSize)
        {
            _sorted = awards.OrderByDescending(a => a.Amount).ToList();
            _pageSize = pageSize;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            var body = JObject.Parse(request.Content!.ReadAsStringAsync(cancellationToken).Result);
            var page = body["page"]!.Value<int>();
            var amounts = (JObject)body["filters"]!["award_amounts"]![0]!;
            var lower = amounts["lower_bound"]!.Value<decimal>();
            var upper = amounts["upper_bound"]?.Value<decimal>();

            MaxPageRequested = Math.Max(MaxPageRequested, page);
            if (upper.HasValue)
            {
                UpperBoundsSeen.Add(upper.Value);
                // The real API's award_amounts validator only accepts whole-dollar
                // values; a fractional bound is a 422. Mirror that so the cursor can
                // never regress to sending cents (the bug that froze the first
                // production cycle of the cursor rollout).
                if (decimal.Ceiling(upper.Value) != upper.Value)
                {
                    return Task.FromResult(
                        new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
                        {
                            Content = new StringContent(
                                $"{{\"detail\": \"Invalid value in 'filters|award_amounts'. "
                                    + $"'{upper.Value}' is not a valid type (dictionary).\"}}"
                            ),
                        }
                    );
                }
            }

            var band = _sorted
                .Where(a => a.Amount >= lower && (!upper.HasValue || a.Amount <= upper.Value))
                .ToList();
            var rows = band.Skip((page - 1) * _pageSize).Take(_pageSize).ToList();
            var hasNext = page * _pageSize < band.Count;

            var payload = new
            {
                results = rows.Select(a => new Dictionary<string, object>
                {
                    ["generated_internal_id"] = a.Id,
                    ["Award ID"] = a.Id,
                    ["Award Amount"] = a.Amount,
                }),
                page_metadata = new { page, hasNext },
            };
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonConvert.SerializeObject(payload)),
                }
            );
        }
    }

    /// <summary>
    /// Simulates a tie run past the API ceiling: for the full window every page returns
    /// rows at one identical amount with hasNext always true; any narrower date range
    /// completes immediately. Records the (start, end) of every window requested.
    /// </summary>
    private sealed class EndlessTieHandler : HttpMessageHandler
    {
        private readonly string _fullWindowStart;
        private readonly string _fullWindowEnd;

        public List<(string Start, string End)> WindowsSeen { get; } = [];

        public EndlessTieHandler(string fullWindowStart, string fullWindowEnd)
        {
            _fullWindowStart = fullWindowStart;
            _fullWindowEnd = fullWindowEnd;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            var body = JObject.Parse(request.Content!.ReadAsStringAsync(cancellationToken).Result);
            var page = body["page"]!.Value<int>();
            var period = (JObject)body["filters"]!["time_period"]![0]!;
            var window = (
                Start: period["start_date"]!.Value<string>()!,
                End: period["end_date"]!.Value<string>()!
            );
            if (!WindowsSeen.Contains(window))
                WindowsSeen.Add(window);

            var isFullWindow = window.Start == _fullWindowStart && window.End == _fullWindowEnd;
            var payload = new
            {
                results = new[]
                {
                    new Dictionary<string, object>
                    {
                        ["generated_internal_id"] = $"TIE_{window.Start}_{window.End}_{page}",
                        ["Award ID"] = $"PIID_{page}",
                        ["Award Amount"] = 5_000_000m,
                    },
                },
                page_metadata = new { page, hasNext = isFullWindow },
            };
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonConvert.SerializeObject(payload)),
                }
            );
        }
    }
}
