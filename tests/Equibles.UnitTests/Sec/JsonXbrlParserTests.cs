using Equibles.Sec.FinancialFacts.BusinessLogic.Parsers;
using FluentAssertions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Equibles.UnitTests.Sec;

public class JsonXbrlParserTests
{
    private static string Fixture =>
        File.ReadAllText(
            Path.Combine(
                AppContext.BaseDirectory,
                "TestAssets",
                "Esef",
                "ctt-2022-json-excerpt.json"
            )
        );

    [Fact]
    public void Parse_CapturedCttFacts_PreservesValueIdentityAndInclusiveDate()
    {
        var facts = new JsonXbrlParser().Parse(Fixture);
        facts.Should().HaveCount(2);
        facts[0].Value.Should().Be(6327424m);
        facts[1].Value.Should().Be(6183979m);
        facts[0].PeriodEnd.Should().Be(new DateOnly(2021, 12, 31));
        facts[1].PeriodEnd.Should().Be(new DateOnly(2022, 12, 31));
        facts
            .Should()
            .OnlyContain(fact =>
                fact.ConsolidatedLei == "529900G4A1IKOKC22K56"
                && fact.Unit == "EUR"
                && fact.IsInstant
                && fact.PeriodStart == fact.PeriodEnd
                && fact.Tag == "InvestmentPropertyCompleted"
                && fact.Taxonomy == "ifrs-full"
                && fact.Dimensions.Count == 0
            );
    }

    [Theory]
    [InlineData("2022-01-01T00:00:00/2023-01-01T00:00:00", true)]
    [InlineData("2023-01-01T00:00:00/2022-01-01T00:00:00", false)]
    [InlineData("2022-01-01T00:00:00/2022-01-01T00:00:00", false)]
    [InlineData("2022-12-31", false)]
    [InlineData("2023-01-01T12:00:00", false)]
    [InlineData("2023-01-01T00:00:00Z", false)]
    [InlineData("0001-01-01T00:00:00", false)]
    [InlineData("forever", false)]
    public void Parse_Periods_RequireSupportedExactMidnightIntervals(string period, bool accepted)
    {
        var root = SingleFact();
        root["facts"]["f-3"]["dimensions"]["period"] = period;
        var facts = new JsonXbrlParser().Parse(root.ToString());
        facts.Should().HaveCount(accepted ? 1 : 0);
        if (accepted)
        {
            facts[0].PeriodStart.Should().Be(new DateOnly(2022, 1, 1));
            facts[0].PeriodEnd.Should().Be(new DateOnly(2022, 12, 31));
            facts[0].IsInstant.Should().BeFalse();
        }
    }

    [Theory]
    [InlineData("123.45", true)]
    [InlineData("-123", true)]
    [InlineData("0", true)]
    [InlineData("12345678901234567890123456789", false)]
    [InlineData("0.00000000000000000000000000001", false)]
    [InlineData("1,234", false)]
    [InlineData("1 234", false)]
    [InlineData("NaN", false)]
    [InlineData("INF", false)]
    [InlineData("1e3", false)]
    public void Parse_Amounts_NeverRoundOrInferFormatting(string value, bool accepted)
    {
        var root = SingleFact();
        root["facts"]["f-3"]["value"] = value;
        root["facts"]["f-3"]["decimals"] = -6;
        var facts = new JsonXbrlParser().Parse(root.ToString());
        facts.Should().HaveCount(accepted ? 1 : 0);
        if (accepted)
            facts[0]
                .Value.Should()
                .Be(decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("ifrs-full:SomeAxis", "ifrs-full:SomeMember")]
    [InlineData("ctt:TypedAxis", "123")]
    [InlineData("ctt:TypedAxis", null)]
    [InlineData("language", "en")]
    public void Parse_QualifiedFacts_AreNeverConsolidated(string dimension, string value)
    {
        var root = SingleFact();
        root["facts"]["f-3"]["dimensions"][dimension] = value;
        new JsonXbrlParser().Parse(root.ToString()).Should().BeEmpty();
    }

    [Theory]
    [InlineData("scheme", "https://example.com/entity")]
    [InlineData("iso4217", "https://example.com/units")]
    [InlineData("ifrs-full", "https://example.com/taxonomy")]
    public void Parse_ForgedNamespaces_AreNotEvidence(string prefix, string uri)
    {
        var root = SingleFact();
        root["documentInfo"]["namespaces"][prefix] = uri;
        new JsonXbrlParser().Parse(root.ToString()).Should().BeEmpty();
    }

    [Theory]
    [InlineData("iso4217:EUR", "EUR")]
    [InlineData("iso4217:EUR/xbrli:shares", "EUR/shares")]
    [InlineData("xbrli:shares", "shares")]
    [InlineData("xbrli:pure", "pure")]
    [InlineData("iso4217:EUR/iso4217:USD", null)]
    [InlineData("iso4217:EUR*xbrli:shares", null)]
    public void Parse_Units_ResolveSourceNamespaces(string unit, string expected)
    {
        var root = SingleFact();
        root["facts"]["f-3"]["dimensions"]["unit"] = unit;
        var facts = new JsonXbrlParser().Parse(root.ToString());
        facts.Should().HaveCount(expected == null ? 0 : 1);
        if (expected != null)
            facts[0].Unit.Should().Be(expected);
    }

    [Fact]
    public void Parse_DuplicateProperties_RejectsWholeReport()
    {
        var invalid = Fixture.Replace(
            "\"value\": \"6327424\"",
            "\"value\": \"1\", \"value\": \"6327424\""
        );
        var parse = () => new JsonXbrlParser().Parse(invalid);
        parse.Should().Throw<JsonReaderException>();
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("<html>not a report</html>")]
    public void Parse_InvalidEnvelope_ThrowsInsteadOfClaimingSuccess(string json)
    {
        var parse = () => new JsonXbrlParser().Parse(json);
        parse.Should().Throw<JsonReaderException>();
    }

    [Theory]
    [InlineData("6327424", 1)]
    [InlineData("999", 0)]
    public void Parse_DuplicateNaturalKeys_RejectsConflictingValues(
        string secondValue,
        int expected
    )
    {
        var root = SingleFact();
        var duplicate = root["facts"]["f-3"].DeepClone();
        duplicate["value"] = secondValue;
        root["facts"]["duplicate"] = duplicate;
        new JsonXbrlParser().Parse(root.ToString()).Should().HaveCount(expected);
    }

    [Fact]
    public void Parse_ConflictingLaterFacts_CannotHideThatTheRequestedPeriodIsComparative()
    {
        var root = JObject.Parse(Fixture);
        var duplicate = root["facts"]["f-4"].DeepClone();
        duplicate["value"] = "999";
        root["facts"]["conflicting-later-fact"] = duplicate;
        var parse = () =>
            new JsonXbrlParser().Parse(
                root.ToString(),
                "529900G4A1IKOKC22K56",
                new DateOnly(2021, 12, 31)
            );
        parse.Should().Throw<InvalidDataException>();
    }

    private static JObject SingleFact()
    {
        var root = JObject.Parse(Fixture);
        ((JObject)root["facts"]).Property("f-4").Remove();
        return root;
    }
}
