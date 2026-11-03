using System.Net;
using Equibles.Integrations.Gleif;
using Microsoft.Extensions.Logging.Abstractions;

namespace Equibles.UnitTests.Gleif;

// GLEIF relates a UCITS sub-fund to its umbrella company; a company with no umbrella answers 404.
public class GleifUmbrellaFundTests
{
    private const string SubFund = "5493006ESS9BM09FB892";

    private static Task<string> Fixture(string name) =>
        File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "TestAssets", "Gleif", name));

    private static GleifIdentityClient Client(HttpClient http) =>
        new(http, NullLogger<GleifIdentityClient>.Instance);

    [Fact]
    public async Task GetUmbrellaFundLei_ReadsTheSubFundsUmbrellaCompany()
    {
        var handler = new ReplyHandler([
            (HttpStatusCode.OK, await Fixture("ishares-sp500-umbrella-fund.json")),
        ]);
        using var http = new HttpClient(handler);

        var umbrella = await Client(http).GetUmbrellaFundLei(SubFund);

        umbrella.Should().Be("549300Q7FFITMZ2PFZ28");
        handler
            .Requests.Should()
            .Equal(new Uri($"https://api.gleif.org/api/v1/lei-records/{SubFund}/umbrella-fund"));
    }

    [Fact]
    public async Task GetUmbrellaFundLei_ReadsNotFoundAsNoUmbrella()
    {
        using var http = new HttpClient(
            new ReplyHandler([
                (HttpStatusCode.NotFound, await Fixture("apple-umbrella-fund-404.json")),
            ])
        );

        (await Client(http).GetUmbrellaFundLei("HWUPKR0MPOU8FGXBT394")).Should().BeNull();
    }

    [Fact]
    public async Task GetUmbrellaFundLei_RefusesAFundNamedAsItsOwnUmbrella()
    {
        var body = (await Fixture("ishares-sp500-umbrella-fund.json")).Replace(
            "549300Q7FFITMZ2PFZ28",
            SubFund
        );
        using var http = new HttpClient(new ReplyHandler([(HttpStatusCode.OK, body)]));

        var umbrella = () => Client(http).GetUmbrellaFundLei(SubFund);

        await umbrella.Should().ThrowAsync<InvalidDataException>();
    }

    private sealed class ReplyHandler(IEnumerable<(HttpStatusCode Status, string Body)> replies)
        : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode Status, string Body)> _replies = new(replies);
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Requests.Add(request.RequestUri);
            if (_replies.Count == 0)
                throw new InvalidOperationException("Unexpected HTTP request.");
            var reply = _replies.Dequeue();
            return Task.FromResult(
                new HttpResponseMessage(reply.Status)
                {
                    Content = new StringContent(reply.Body),
                    RequestMessage = request,
                }
            );
        }
    }
}
