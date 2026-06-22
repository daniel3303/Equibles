using Equibles.Integrations.GovernmentContracts.Models;
using FluentAssertions;
using Newtonsoft.Json;
using Xunit;

namespace Equibles.UnitTests.GovernmentContracts;

public class UsaSpendingCodeConverterTests
{
    // Contract (from the converter's doc): a NAICS/PSC field always yields the bare code
    // string "(or null)". An object that carries a description but no "code" key has no
    // bare code, so it must deserialize to null — not the description, not the raw object.
    [Fact]
    public void ReadJson_WhenObjectHasNoCodeKey_YieldsNull()
    {
        const string json = """
            { "NAICS": { "description": "FACILITIES SUPPORT SERVICES" } }
            """;

        var record = JsonConvert.DeserializeObject<UsaSpendingAwardRecord>(json);

        record!.Naics.Should().BeNull();
    }
}
