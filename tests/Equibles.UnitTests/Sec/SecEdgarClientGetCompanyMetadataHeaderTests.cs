using System.Net;
using Equibles.Integrations.Sec;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// The classification fields of EDGAR's submissions header, captured from JPMorgan Chase's
/// (CIK 19617) record: SIC, SIC description, filer category, state of incorporation and the CIK
/// the reply names.
/// </summary>
public class SecEdgarClientGetCompanyMetadataHeaderTests
{
    private const string Submissions = """
        {"cik":"0000019617","entityType":"operating","sic":"6021","sicDescription":"National Commercial Banks","ownerOrg":"02 Finance","name":"JPMORGAN CHASE & CO","tickers":["JPM"],"exchanges":["NYSE"],"ein":"132624428","lei":null,"category":"Large accelerated filer","fiscalYearEnd":"1231","stateOfIncorporation":"DE","stateOfIncorporationDescription":"DE","filings":{"recent":{},"files":[]}}
        """;

    [Fact]
    public async Task GetCompanyMetadata_MapsTheClassificationFields()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string> { ["Sec:ContactEmail"] = "test@example.com" }
            )
            .Build();
        var sut = new SecEdgarClient(
            new HttpClient(new SubmissionsHandler()),
            Substitute.For<ILogger<SecEdgarClient>>(),
            config
        );

        var metadata = await sut.GetCompanyMetadata("19617");

        metadata.FilerCik.Should().Be("0000019617");
        metadata.Sic.Should().Be("6021");
        metadata.SicDescription.Should().Be("National Commercial Banks");
        metadata.Category.Should().Be("Large accelerated filer");
        metadata.StateOfIncorporation.Should().Be("DE");
        metadata.EntityType.Should().Be("operating");
    }

    private sealed class SubmissionsHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(Submissions),
                }
            );
    }
}
