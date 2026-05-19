using System.Net;
using System.Text;
using Equibles.Integrations.Sec;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Happy-path coverage for <see cref="SecEdgarClient.GetCompanyFacts"/>. The
/// only prior test pinned the 404 branch; the JSON → <c>CompanyFactsResponse</c>
/// mapping (taxonomy → tag → unit nesting, every scalar, and the
/// duration-vs-instant distinction expressed as a present-or-absent
/// <c>start</c>) was unvalidated. A silent drift in those JsonProperty names or
/// the <c>DateOnly?</c> mapping would surface only as zero ingested facts far
/// downstream, so this asserts the full shape against a representative payload
/// carrying both a duration concept (Revenues/USD, has <c>start</c>) and an
/// instant concept (Assets/USD, no <c>start</c>).
/// </summary>
public class SecEdgarClientGetCompanyFactsParseTests
{
    private const string CompanyFactsJson = """
        {
          "cik": 320193,
          "entityName": "Apple Inc.",
          "facts": {
            "us-gaap": {
              "Revenues": {
                "label": "Revenues",
                "description": "Total revenue from customers.",
                "units": {
                  "USD": [
                    {
                      "start": "2023-01-01",
                      "end": "2023-12-31",
                      "val": 383285000000,
                      "accn": "0000320193-24-000123",
                      "fy": 2023,
                      "fp": "FY",
                      "form": "10-K",
                      "filed": "2024-01-15",
                      "frame": "CY2023"
                    }
                  ]
                }
              },
              "Assets": {
                "label": "Assets",
                "description": "Total assets.",
                "units": {
                  "USD": [
                    {
                      "end": "2023-12-31",
                      "val": 352583000000,
                      "accn": "0000320193-24-000123",
                      "fy": 2023,
                      "fp": "FY",
                      "form": "10-K",
                      "filed": "2024-01-15"
                    }
                  ]
                }
              }
            }
          }
        }
        """;

    [Fact]
    public async Task GetCompanyFacts_ValidPayload_MapsEveryFieldAndDistinguishesInstantFromDuration()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string> { ["Sec:ContactEmail"] = "test@example.com" }
            )
            .Build();
        var sut = new SecEdgarClient(
            new HttpClient(new JsonHandler(CompanyFactsJson)),
            Substitute.For<ILogger<SecEdgarClient>>(),
            config
        );

        var facts = await sut.GetCompanyFacts("0000320193");

        facts.Should().NotBeNull();
        facts.Cik.Should().Be(320193);
        facts.EntityName.Should().Be("Apple Inc.");
        facts.Facts.Should().ContainKey("us-gaap");

        var usGaap = facts.Facts["us-gaap"];
        usGaap.Should().ContainKeys("Revenues", "Assets");

        // Duration concept — `start` present, mapped to a non-null DateOnly.
        var revenue = usGaap["Revenues"];
        revenue.Label.Should().Be("Revenues");
        revenue.Description.Should().Be("Total revenue from customers.");
        revenue.Units.Should().ContainKey("USD");
        var revenueValue = revenue.Units["USD"].Should().ContainSingle().Subject;
        revenueValue.Start.Should().Be(new DateOnly(2023, 1, 1));
        revenueValue.End.Should().Be(new DateOnly(2023, 12, 31));
        revenueValue.Val.Should().Be(383285000000m);
        revenueValue.Accn.Should().Be("0000320193-24-000123");
        revenueValue.Fy.Should().Be(2023);
        revenueValue.Fp.Should().Be("FY");
        revenueValue.Form.Should().Be("10-K");
        revenueValue.Filed.Should().Be(new DateOnly(2024, 1, 15));
        revenueValue.Frame.Should().Be("CY2023");

        // Instant concept — `start` absent, must map to null so the ingest
        // pipeline classifies it as a balance-sheet (instant) fact.
        var assetsValue = usGaap["Assets"].Units["USD"].Should().ContainSingle().Subject;
        assetsValue.Start.Should().BeNull();
        assetsValue.End.Should().Be(new DateOnly(2023, 12, 31));
        assetsValue.Val.Should().Be(352583000000m);
        assetsValue.Frame.Should().BeNull();
    }

    private sealed class JsonHandler : HttpMessageHandler
    {
        private readonly string _json;

        public JsonHandler(string json) => _json = json;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(_json, Encoding.UTF8, "application/json"),
                }
            );
    }
}
