using System.Net;
using Equibles.Integrations.Common.RateLimiter;
using Equibles.Integrations.OpenFigi;
using Microsoft.Extensions.Logging.Abstractions;

namespace Equibles.UnitTests.OpenFigi;

public class OpenFigiClientTests
{
    private static readonly IRateLimiter Unpaced = new RateLimiter(
        100,
        TimeSpan.FromMilliseconds(1)
    );

    private static Task<string> Fixture(string name) =>
        File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "TestAssets", "OpenFigi", name)
        );

    private static OpenFigiClient Client(HttpMessageHandler handler) =>
        new(new HttpClient(handler), NullLogger<OpenFigiClient>.Instance) { Pace = Unpaced };

    private static IReadOnlyList<OpenFigiJob> TokyoJobs() =>
        [
            new("TICKER", "7203", "JT"),
            new("TICKER", "8951", "JT"),
            new("TICKER", "130A", "JT"),
            new("ID_ISIN", "JP3633400001", "JT"),
            new("TICKER", "0000", "JT"),
        ];

    [Fact]
    public async Task Map_SendsTheRecordedJobsAndAnswersEachInOrder()
    {
        var handler = new ReplyHandler([(HttpStatusCode.OK, await Fixture("tokyo-response.json"))]);

        var answers = await Client(handler).Map(TokyoJobs(), CancellationToken.None);

        handler.Bodies.Should().Equal(await Fixture("tokyo-request.json"));
        handler.Requests.Should().Equal(OpenFigiClient.MappingUrl);
        answers.Should().HaveCount(5);
        answers.Should().AllSatisfy(answer => answer.Error.Should().BeNull());
        var toyota = answers[0].Instruments.Should().ContainSingle().Subject;
        ((string)toyota["ticker"], (string)toyota["exchCode"], (string)toyota["securityType2"])
            .Should()
            .Be(("7203", "JT", "Common Stock"));
        ((string)toyota["compositeFIGI"], (string)toyota["shareClassFIGI"])
            .Should()
            .Be(("BBG000BCM915", "BBG001S6BGT4"));
        // A Tokyo REIT is not a common share, and an alphanumeric local code maps like a numeric one.
        ((string)answers[1].Instruments.Single()["securityType2"])
            .Should()
            .Be("REIT");
        ((string)answers[2].Instruments.Single()["securityType2"]).Should().Be("Common Stock");
        // The ISIN maps to the same Tokyo line as its local code.
        ((string)answers[3].Instruments.Single()["shareClassFIGI"])
            .Should()
            .Be("BBG001S6BGT4");
        // OpenFIGI's "No identifier found." is a definitive empty answer, not a refusal.
        answers[4].Instruments.Should().BeEmpty();
    }

    [Fact]
    public async Task Map_ARefusedJobNeverDecidesItsBatch()
    {
        var handler = new ReplyHandler([
            (HttpStatusCode.OK, """[{"error":"Invalid idValue format"},{"data":[]}]"""),
        ]);

        var answers = await Client(handler)
            .Map(
                [new("ID_ISIN", "BAD", "JT"), new("TICKER", "7203", "JT")],
                CancellationToken.None
            );

        (answers[0].Error, answers[0].Instruments).Should().Be(("Invalid idValue format", null));
        answers[1].Instruments.Should().BeEmpty();
    }

    [Theory]
    [InlineData("""[{"data":[]}]""")]
    [InlineData("""{"error":"Too many requests"}""")]
    [InlineData("""["7203","8951"]""")]
    [InlineData("not json")]
    public async Task Map_RefusesAnAnswerThatDoesNotMatchTheJobs(string body)
    {
        var handler = new ReplyHandler([(HttpStatusCode.OK, body)]);

        var map = () =>
            Client(handler)
                .Map(
                    [new("TICKER", "7203", "JT"), new("TICKER", "8951", "JT")],
                    CancellationToken.None
                );

        await map.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task Map_CarriesOneToTenJobs_WithoutARequestOtherwise()
    {
        var handler = new ReplyHandler([]);
        var client = Client(handler);

        var none = () => client.Map([], CancellationToken.None);
        var eleven = () =>
            client.Map(
                Enumerable
                    .Range(0, OpenFigiClient.JobsPerRequest + 1)
                    .Select(index => new OpenFigiJob("TICKER", "T" + index, "JT"))
                    .ToList(),
                CancellationToken.None
            );

        await none.Should().ThrowAsync<ArgumentException>();
        await eleven.Should().ThrowAsync<ArgumentException>();
        handler.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("TICKER|GLAS/A/U|CN", "TICKER", "GLAS/A/U", "CN")]
    [InlineData(
        "ID_BB_GLOBAL_SHARE_CLASS_LEVEL|BBG001S6BGT4|JT",
        "ID_BB_GLOBAL_SHARE_CLASS_LEVEL",
        "BBG001S6BGT4",
        "JT"
    )]
    public void JobKey_RoundTrips(string key, string type, string value, string exchange)
    {
        var job = OpenFigiJob.Parse(key);

        job.Should().Be(new OpenFigiJob(type, value, exchange));
        job.Key.Should().Be(key);
    }

    [Fact]
    public void JobKey_RefusesAnotherShape()
    {
        var parse = () => OpenFigiJob.Parse("TICKER|7203");

        parse.Should().Throw<ArgumentException>();
    }

    // Serves each queued status and body in order, recording each request and its body.
    private sealed class ReplyHandler(IEnumerable<(HttpStatusCode Status, string Body)> replies)
        : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode Status, string Body)> _replies = new(replies);
        public List<Uri> Requests { get; } = [];
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Requests.Add(request.RequestUri);
            Bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            if (_replies.Count == 0)
                throw new InvalidOperationException("Unexpected HTTP request.");
            var reply = _replies.Dequeue();
            return new HttpResponseMessage(reply.Status)
            {
                Content = new StringContent(reply.Body),
                RequestMessage = request,
            };
        }
    }
}
