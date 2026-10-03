using System.Net;
using Equibles.Integrations.Sec;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Equibles.UnitTests.Sec;

public class SecEdgarClientFormerNamesTests
{
    // Identity fields recorded from data.sec.gov/submissions/CIK0000101594.json.
    private static string RecordedIdentity =>
        File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "TestAssets", "Sec", "big-sky-former-names.json")
        );

    [Fact]
    public async Task GetFormerCompanyNames_RecordedPayload_PreservesNamesAndSourceDates()
    {
        var handler = new SubmissionsHandler(RecordedIdentity);
        var client = BuildClient(handler);

        var names = await client.GetFormerCompanyNames("101594");

        var name = names.Should().ContainSingle().Subject;
        name.Name.Should().Be("US ENERGY CORP");
        name.From.Should().Be("1995-04-17T04:00:00.000Z");
        name.To.Should().Be("2026-05-15T04:00:00.000Z");
        handler.Paths.Should().Equal("/submissions/CIK0000101594.json");
    }

    [Fact]
    public async Task GetFormerCompanyNames_ReusesSameIssuerCache_ButFetchesAnotherIssuer()
    {
        var handler = new SubmissionsHandler(RecordedIdentity);
        var client = BuildClient(handler);
        await client.GetCompanyMetadata("101594");

        await client.GetFormerCompanyNames("101594");
        var otherIssuer = () => client.GetFormerCompanyNames("123456");

        await otherIssuer.Should().ThrowAsync<InvalidDataException>();
        handler
            .Paths.Should()
            .Equal("/submissions/CIK0000101594.json", "/submissions/CIK0000123456.json");
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("{\"cik\":\"123456\",\"formerNames\":[{\"name\":\"Another issuer\"}]}")]
    public async Task GetFormerCompanyNames_UnverifiedPayloadIdentity_Refuses(string body)
    {
        var client = BuildClient(new SubmissionsHandler(body));

        var act = () => client.GetFormerCompanyNames("101594");

        await act.Should().ThrowAsync<InvalidDataException>();
    }

    [Theory]
    [InlineData("{\"cik\":101594}")]
    [InlineData("{\"cik\":\"101594\",\"formerNames\":null}")]
    public async Task GetFormerCompanyNames_NoFormerNames_ReturnsEmpty(string body)
    {
        var client = BuildClient(new SubmissionsHandler(body));

        (await client.GetFormerCompanyNames("101594")).Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0000000000")]
    [InlineData("12345678901")]
    [InlineData("../101594")]
    public async Task GetFormerCompanyNames_InvalidCik_DoesNotFetch(string cik)
    {
        var handler = new SubmissionsHandler(RecordedIdentity);
        var client = BuildClient(handler);

        var act = () => client.GetFormerCompanyNames(cik);

        await act.Should().ThrowAsync<ArgumentException>();
        handler.Paths.Should().BeEmpty();
    }

    [Fact]
    public async Task GetFormerCompanyNames_CancelledEvenWhenCached_DoesNotReturnEvidence()
    {
        var client = BuildClient(new SubmissionsHandler(RecordedIdentity));
        await client.GetCompanyMetadata("101594");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var act = () => client.GetFormerCompanyNames("101594", cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task GetFormerCompanyNames_RequestFailure_DoesNotBecomeEmptyHistory()
    {
        var handler = new SubmissionsHandler("", HttpStatusCode.NotFound);
        var client = BuildClient(handler);

        var act = () => client.GetFormerCompanyNames("101594");

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    private static SecEdgarClient BuildClient(SubmissionsHandler handler) =>
        new(
            new HttpClient(handler),
            NullLogger<SecEdgarClient>.Instance,
            new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<string, string> { ["Sec:ContactEmail"] = "test@example.com" }
                )
                .Build()
        );

    private sealed class SubmissionsHandler(string body, HttpStatusCode status = HttpStatusCode.OK)
        : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Paths.Add(request.RequestUri.AbsolutePath);
            return Task.FromResult(
                new HttpResponseMessage(status) { Content = new StringContent(body) }
            );
        }
    }
}
