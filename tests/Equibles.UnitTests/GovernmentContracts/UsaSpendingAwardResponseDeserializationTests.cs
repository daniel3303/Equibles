using Equibles.Integrations.GovernmentContracts.Models;
using FluentAssertions;
using Newtonsoft.Json;
using Xunit;

namespace Equibles.UnitTests.GovernmentContracts;

public class UsaSpendingAwardResponseDeserializationTests
{
    // USAspending returns NAICS and PSC as objects ({ code, description }), not bare
    // strings. The response must deserialize and expose the bare code for each.
    [Fact]
    public void Deserialize_WhenNaicsAndPscAreObjects_ExposesTheBareCode()
    {
        const string json = """
            {
              "results": [
                {
                  "generated_internal_id": "CONT_AWD_DEAC0494AL85000",
                  "Award ID": "DEAC0494AL85000",
                  "Award Amount": 48063763681.32,
                  "NAICS": { "code": "561210", "description": "FACILITIES SUPPORT SERVICES" },
                  "PSC": { "code": "M181", "description": "OPER OF GOVT R&D GOCO FACILITIES" },
                  "Recipient Name": "LOCKHEED MARTIN CORP"
                }
              ],
              "page_metadata": { "page": 1, "hasNext": false }
            }
            """;

        var response = JsonConvert.DeserializeObject<UsaSpendingAwardResponse>(json);

        response.Should().NotBeNull();
        response!.Results.Should().HaveCount(1);
        var record = response.Results[0];
        record.Naics.Should().Be("561210");
        record.Psc.Should().Be("M181");
    }

    // The bare-string shape must keep working, so the client tolerates either form.
    [Fact]
    public void Deserialize_WhenNaicsAndPscAreStrings_KeepsTheValue()
    {
        const string json = """
            {
              "results": [
                {
                  "generated_internal_id": "CONT_AWD_ABC",
                  "Award ID": "ABC",
                  "NAICS": "336411",
                  "PSC": "1510"
                }
              ],
              "page_metadata": { "page": 1, "hasNext": false }
            }
            """;

        var response = JsonConvert.DeserializeObject<UsaSpendingAwardResponse>(json);

        response!.Results[0].Naics.Should().Be("336411");
        response.Results[0].Psc.Should().Be("1510");
    }
}
