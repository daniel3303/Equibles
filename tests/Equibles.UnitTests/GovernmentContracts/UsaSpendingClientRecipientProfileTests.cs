using System.Net;
using Equibles.Integrations.GovernmentContracts;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Equibles.UnitTests.GovernmentContracts;

// Contract: the recipient-profile read is a GET on the level-qualified recipient hash that
// keeps the client's wire rules (fresh connection per request), maps the corporate-family
// fields, and answers a 404 as null — an unknown or profileless recipient is an answer,
// not a fault, and turning it into an exception would fail whole import windows over
// recipients that simply have no SAM family record.
public class UsaSpendingClientRecipientProfileTests
{
    [Fact]
    public async Task GetRecipientProfile_MapsTheCorporateFamilyFields()
    {
        var handler = new CapturingHandler(
            HttpStatusCode.OK,
            """
            {
              "recipient_id": "abc123-C",
              "name": "CACI, INC. - FEDERAL",
              "recipient_level": "C",
              "parent_id": "def456-P",
              "parent_name": "CACI INTERNATIONAL INC",
              "parents": [
                { "parent_id": "def456-P", "parent_name": "CACI INTERNATIONAL INC" }
              ]
            }
            """
        );
        var sut = NewClient(handler);

        var profile = await sut.GetRecipientProfile("abc123-C");

        handler.RequestUri.Should().Be("https://api.usaspending.gov/api/v2/recipient/abc123-C/");
        handler.Method.Should().Be(HttpMethod.Get);
        handler
            .ConnectionClose.Should()
            .BeTrue("profile reads share the search endpoint's fresh-connection rule");
        profile.Should().NotBeNull();
        profile.RecipientLevel.Should().Be("C");
        profile.ParentId.Should().Be("def456-P");
        profile.ParentName.Should().Be("CACI INTERNATIONAL INC");
        profile.Parents.Should().ContainSingle(p => p.ParentId == "def456-P");
    }

    [Fact]
    public async Task GetRecipientProfile_NotFound_IsNullNotAFault()
    {
        var sut = NewClient(new CapturingHandler(HttpStatusCode.NotFound, "{}"));

        var profile = await sut.GetRecipientProfile("unknown-hash-R");

        profile.Should().BeNull();
    }

    private static UsaSpendingClient NewClient(HttpMessageHandler handler) =>
        new(new HttpClient(handler), Substitute.For<ILogger<UsaSpendingClient>>());

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _body;

        public string RequestUri { get; private set; }
        public HttpMethod Method { get; private set; }
        public bool? ConnectionClose { get; private set; }

        public CapturingHandler(HttpStatusCode statusCode, string body)
        {
            _statusCode = statusCode;
            _body = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            RequestUri = request.RequestUri?.ToString();
            Method = request.Method;
            ConnectionClose = request.Headers.ConnectionClose;
            return Task.FromResult(
                new HttpResponseMessage(_statusCode) { Content = new StringContent(_body) }
            );
        }
    }
}
