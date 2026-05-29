using System.Globalization;
using System.Net;
using Equibles.Integrations.Sec;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.UnitTests.Sec;

public class SecEdgarClientGetArchiveFilingsToDateSkipCultureTests
{
    // GetArchiveFilings prunes an archive whose ISO filingFrom is after the
    // requested toDate WITHOUT a second HTTP fetch (pinned under invariant
    // culture by the sibling skip test). The prune parses filingFrom with a
    // bare DateOnly.TryParse (no IFormatProvider), so under a non-Gregorian
    // host calendar (ar-SA) the ISO date fails to parse, the skip guard
    // short-circuits, and the out-of-window archive is fetched anyway —
    // needless SEC load. The contract is host-culture-independent pruning.
    [Fact]
    public async Task GetCompanyFilings_ArchiveFileAfterToDate_UnderHijriCulture_StillSkipsWithoutFetch()
    {
        var json =
            "{\"cik\":\"1234567\",\"filings\":{\"recent\":{},"
            + "\"files\":[{\"name\":\"CIK0001234567-submissions-001.json\","
            + "\"filingFrom\":\"2030-01-01\",\"filingTo\":\"2031-12-31\"}]}}";
        var handler = new CountingJsonHandler(json);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string> { ["Sec:ContactEmail"] = "test@example.com" }
            )
            .Build();
        var sut = new SecEdgarClient(
            new HttpClient(handler),
            Substitute.For<ILogger<SecEdgarClient>>(),
            config
        );

        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("ar-SA");
            await sut.GetCompanyFilings("0001234567", toDate: new DateOnly(2020, 12, 31));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }

        // Exactly one HTTP call (the submissions index): the out-of-window
        // archive must be pruned, not fetched — regardless of host culture.
        handler
            .CallCount.Should()
            .Be(
                1,
                "the ISO filingFrom must parse under any host calendar so the toDate prune fires; under ar-SA the bare DateOnly.TryParse fails and the archive is fetched anyway"
            );
    }

    private sealed class CountingJsonHandler : HttpMessageHandler
    {
        private readonly string _json;
        public int CallCount { get; private set; }

        public CountingJsonHandler(string json) => _json = json;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            CallCount++;
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(_json) }
            );
        }
    }
}
