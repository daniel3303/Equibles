using Equibles.EquityMarkets.BusinessLogic.Directory;
using Newtonsoft.Json.Linq;

namespace Equibles.UnitTests.EquityMarkets;

// The answers are OpenFIGI's own, as it returned them for each ISIN's US composite and over-the-counter jobs.
public class IsinShareClassAnswerTests
{
    private const string AirbusComposite = """
        [{"figi":"BBG000BJJR23","name":"AIRBUS SE","ticker":"EADSF","exchCode":"US","compositeFIGI":"BBG000BJJR23","securityType":"Common Stock","marketSector":"Equity","shareClassFIGI":"BBG001S8TFZ6","securityType2":"Common Stock","securityDescription":"EADSF"}]
        """;
    private const string AirbusOverTheCounter = """
        [{"figi":"BBG000BJJYG3","name":"AIRBUS SE","ticker":"EADSF","exchCode":"UV","compositeFIGI":"BBG000BJJR23","securityType":"Common Stock","marketSector":"Equity","shareClassFIGI":"BBG001S8TFZ6","securityType2":"Common Stock","securityDescription":"EADSF"}]
        """;
    private const string FerrariComposite = """
        [{"figi":"BBG009PH3Q86","name":"FERRARI NV","ticker":"RACE","exchCode":"US","compositeFIGI":"BBG009PH3Q86","securityType":"Common Stock","marketSector":"Equity","shareClassFIGI":"BBG009PH3Q95","securityType2":"Common Stock","securityDescription":"RACE"}]
        """;

    [Fact]
    public void AShareQuotedOnlyOverTheCounter_StatesItsUsLine()
    {
        var answer = IsinShareClassAnswer.Read(
            "NL0000235190",
            JArray.Parse(AirbusComposite),
            JArray.Parse(AirbusOverTheCounter)
        );

        answer.ShareClassFigi.Should().Be("BBG001S8TFZ6");
        answer.UsTickers.Should().Equal("EADSF");
        answer.UsSecurityType.Should().Be("Common Stock");
        answer.QuotedOverTheCounter.Should().BeTrue();
        JObject.Parse(answer.PayloadJson)["overTheCounter"]![0]!["figi"]!
            .Value<string>()
            .Should()
            .Be("BBG000BJJYG3");
    }

    // Ferrari's one share trades on the New York Stock Exchange, so OpenFIGI maps it to no over-the-counter line.
    [Fact]
    public void AnExchangeListedShare_IsNotQuotedOverTheCounter()
    {
        var answer = IsinShareClassAnswer.Read("NL0011585146", JArray.Parse(FerrariComposite), []);

        answer.UsTickers.Should().Equal("RACE");
        answer.QuotedOverTheCounter.Should().BeFalse();
    }

    [Fact]
    public void AnIsinWithoutAUsLine_StatesNothing()
    {
        var answer = IsinShareClassAnswer.Read("INE296A01032", [], []);

        answer.ShareClassFigi.Should().BeNull();
        answer.UsTickers.Should().BeEmpty();
        answer.UsSecurityType.Should().BeNull();
        answer.QuotedOverTheCounter.Should().BeFalse();
    }

    // A second US ticker the over-the-counter venue does not quote leaves room for an exchange line.
    [Fact]
    public void ASecondUsTickerWithoutAQuote_IsNotQuotedOverTheCounter()
    {
        var composite = JArray.Parse(AirbusComposite);
        var second = (JObject)composite[0].DeepClone();
        second["ticker"] = "EADSY";
        composite.Add(second);

        var answer = IsinShareClassAnswer.Read(
            "NL0000235190",
            composite,
            JArray.Parse(AirbusOverTheCounter)
        );

        answer.UsTickers.Should().Equal("EADSF", "EADSY");
        answer.QuotedOverTheCounter.Should().BeFalse();
    }

    // A quote under another share class is another security's line.
    [Fact]
    public void AQuoteUnderAnotherShareClass_DoesNotCount()
    {
        var quoted = JArray.Parse(AirbusOverTheCounter);
        quoted[0]["shareClassFIGI"] = "BBG009PH3Q95";

        IsinShareClassAnswer
            .Read("NL0000235190", JArray.Parse(AirbusComposite), quoted)
            .QuotedOverTheCounter.Should()
            .BeFalse();
    }
}
