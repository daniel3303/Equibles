using Equibles.Sec.FinancialFacts.BusinessLogic;
using Equibles.Sec.FinancialFacts.BusinessLogic.Models;
using Equibles.Sec.FinancialFacts.BusinessLogic.Parsers;

namespace Equibles.UnitTests.Esef;

public class EsefAnnualPeriodTests
{
    private const string Lei = "549300JUGBT2EH17X827";
    private static readonly DateOnly End = new(2025, 12, 31);

    [Fact]
    public void IsProvenInline_RecordedAnnualReport_ProvesItsPeriod()
    {
        var html = File.ReadAllText(
            Path.Combine(
                AppContext.BaseDirectory,
                "TestAssets",
                "Esef",
                "ennogie-2025-annual-excerpt.xhtml"
            )
        );

        EsefAnnualPeriod.IsProvenInline(html, Lei, End).Should().BeTrue();
    }

    [Fact]
    public void IsProven_RecordedInterimWithAnnualComparatives_DoesNotProveAnnualCurrentPeriod()
    {
        var facts = new InlineXbrlParser().Parse(
            File.ReadAllText(
                Path.Combine(
                    AppContext.BaseDirectory,
                    "TestAssets",
                    "Esef",
                    "ennogie-2026-interim-excerpt.xhtml"
                )
            )
        );

        facts.Should().HaveCount(4);
        EsefAnnualPeriod.IsProven(facts, Lei, new DateOnly(2026, 6, 30)).Should().BeFalse();
        EsefAnnualPeriod.IsProven(facts, Lei, End).Should().BeFalse();
    }

    [Theory]
    [InlineData(349, false)]
    [InlineData(350, true)]
    [InlineData(364, true)]
    [InlineData(365, true)]
    [InlineData(370, true)]
    [InlineData(380, true)]
    [InlineData(381, false)]
    [InlineData(180, false)]
    [InlineData(90, false)]
    public void IsProven_RequiresAnnualDurationAndMatchingBalanceSheet(int days, bool expected)
    {
        EsefAnnualPeriod.IsProven([Fact(false, days), Fact(true)], Lei, End).Should().Be(expected);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void IsProven_OnePeriodKindAloneIsInsufficient(bool instant)
    {
        EsefAnnualPeriod.IsProven([Fact(instant)], Lei, End).Should().BeFalse();
    }

    [Theory]
    [InlineData("other", "http://xbrl.ifrs.org/taxonomy/2025/ifrs-full")]
    [InlineData(Lei, "http://example.org/taxonomy/2025/ifrs-full")]
    [InlineData(Lei, "http://xbrl.ifrs.org/taxonomy/2025/custom")]
    public void IsProven_RejectsWrongIdentityOrTaxonomy(string issuer, string taxonomy)
    {
        EsefAnnualPeriod
            .IsProven([Fact(false, issuer: issuer, taxonomy: taxonomy), Fact(true)], Lei, End)
            .Should()
            .BeFalse();
    }

    [Fact]
    public void IsProven_ConflictingAnnualValuesDoNotSupplyEvidence()
    {
        EsefAnnualPeriod
            .IsProven([Fact(false), Fact(false, value: 2), Fact(true)], Lei, End)
            .Should()
            .BeFalse();
    }

    [Fact]
    public void IsProven_IdenticalDuplicatesKeepEvidence()
    {
        EsefAnnualPeriod
            .IsProven([Fact(false), Fact(false), Fact(true)], Lei, End)
            .Should()
            .BeTrue();
    }

    [Fact]
    public void IsProven_QualifiedAnnualFactsDoNotSupplyEvidence()
    {
        var annual = Fact(false);
        annual.Dimensions.Add(new ParsedXbrlDimension { Axis = "axis", Member = "member" });

        EsefAnnualPeriod.IsProven([annual, Fact(true)], Lei, End).Should().BeFalse();
    }

    [Theory]
    [InlineData("valid", true)]
    [InlineData("utf8-bom", true)]
    [InlineData("unbound-unit", false)]
    [InlineData("spoofed-unit", false)]
    [InlineData("instance", false)]
    [InlineData("inline", false)]
    [InlineData("rebound", false)]
    [InlineData("uppercase-instance", false)]
    [InlineData("unbound-qname", false)]
    [InlineData("case-collision", false)]
    [InlineData("aliased-scenario", false)]
    [InlineData("aliased-segment", false)]
    [InlineData("default-scenario", false)]
    [InlineData("unrelated-default", true)]
    [InlineData("malformed", false)]
    public void IsProvenInline_RequiresFaithfulXmlNamespaceResolution(string shape, bool expected)
    {
        var html = AnnualInline();
        html = shape switch
        {
            "utf8-bom" => "\uFEFF" + html,
            "unbound-unit" => html.Replace("iso4217:DKK", "undeclared:DKK"),
            "spoofed-unit" => html.Replace("iso4217:DKK", "ifrs-full:DKK"),
            "instance" => html.Replace(
                "http://www.xbrl.org/2003/instance",
                "https://example.org/instance"
            ),
            "inline" => html.Replace(
                "http://www.xbrl.org/2013/inlineXBRL",
                "https://example.org/inline"
            ),
            "rebound" => html.Replace(
                "<div>",
                "<div xmlns:ifrs-full=\"https://example.org/ifrs-full\">"
            ),
            "uppercase-instance" => html.Replace("xbrli", "XBRLI")
                .Replace("http://www.xbrl.org/2003/instance", "https://example.org/instance"),
            "unbound-qname" => html.Replace("name=\"ifrs-full:", "name=\"IFRS-FULL:"),
            "case-collision" => html.Replace(
                "<div>",
                "<div xmlns:IFRS-FULL=\"https://example.org/ifrs-full\">"
            ),
            "aliased-scenario" => html.Replace(
                "</xbrli:context>",
                "<q:scenario xmlns:q=\"http://www.xbrl.org/2003/instance\"><qualifier xmlns=\"https://example.org/qualifier\">segment</qualifier></q:scenario></xbrli:context>"
            ),
            "aliased-segment" => html.Replace(
                "</xbrli:entity>",
                "<q:segment xmlns:q=\"http://www.xbrl.org/2003/instance\"><qualifier xmlns=\"https://example.org/qualifier\">segment</qualifier></q:segment></xbrli:entity>"
            ),
            "default-scenario" => html.Replace(
                "</xbrli:context>",
                "<scenario xmlns=\"http://www.xbrl.org/2003/instance\"><qualifier xmlns=\"https://example.org/qualifier\">segment</qualifier></scenario></xbrli:context>"
            ),
            "unrelated-default" => html.Replace(
                "<div>",
                "<div><svg xmlns=\"http://www.w3.org/2000/svg\"/>"
            ),
            "malformed" => html.Replace("</html>", ""),
            _ => html,
        };

        EsefAnnualPeriod.IsProvenInline(html, Lei, End).Should().Be(expected);
    }

    [Fact]
    public void IsProvenInline_AcceptsLargeReportsWithinTheExtractionBudget()
    {
        var html = AnnualInline()
            .Replace("<body>", "<body><!--" + new string(' ', 51 * 1024 * 1024) + "-->");

        EsefAnnualPeriod.IsProvenInline(html, Lei, End).Should().BeTrue();
    }

    [Theory]
    [InlineData("xbrli:pure", "pure")]
    [InlineData("xbrli:shares", "shares")]
    [InlineData("iso4217:EUR", "EUR")]
    public void Parse_ResolvesSupportedMeasureNamespaces(string measure, string unit)
    {
        var facts = EsefInlineXbrlParser.Parse(AnnualInline().Replace("iso4217:DKK", measure));

        facts.Should().HaveCount(2);
        facts.Should().OnlyContain(fact => fact.Unit == unit);
    }

    [Theory]
    [InlineData("xbrli:shares", true)]
    [InlineData("undeclared:shares", false)]
    [InlineData("ifrs-full:shares", false)]
    public void TryParse_ValidatesDividedUnitDenominator(string denominator, bool expected)
    {
        var html = AnnualInline()
            .Replace(
                "<xbrli:measure>iso4217:DKK</xbrli:measure>",
                $"""
                <xbrli:divide>
                  <xbrli:unitNumerator><xbrli:measure>iso4217:DKK</xbrli:measure></xbrli:unitNumerator>
                  <xbrli:unitDenominator><xbrli:measure>{denominator}</xbrli:measure></xbrli:unitDenominator>
                </xbrli:divide>
                """
            );

        EsefInlineXbrlParser.TryParse(html, out var facts).Should().Be(expected);
        if (expected)
            facts.Should().OnlyContain(fact => fact.Unit == "DKK/shares");
        else
            facts.Should().BeEmpty();
    }

    // An emissions or energy disclosure tagged in a registry unit must not refuse the whole report.
    [Theory]
    [InlineData("http://www.xbrl.org/2009/utr", "tCO2e", true)]
    [InlineData("http://www.xbrl.org/2009/utr", "MWh", true)]
    [InlineData("http://www.xbrl.org/2009/utr", "MVA", false)]
    [InlineData("http://www.xbrl.org/2009/utr", "shares", false)]
    [InlineData("http://www.xbrl.org/2009/utr", "Pure", false)]
    [InlineData("https://example.org/units", "tCO2e", false)]
    public void TryParse_AcceptsRegistryUnitsThatCannotReadAsReservedUnits(
        string unitNamespace,
        string unit,
        bool expected
    )
    {
        var html = AnnualInline()
            .Replace(
                "</ix:resources>",
                $"""
                <xbrli:unit id="emissions"><xbrli:measure xmlns:utr="{unitNamespace}">utr:{unit}</xbrli:measure></xbrli:unit>
                </ix:resources>
                """
            )
            .Replace(
                "</div>",
                """
                <ix:nonFraction xmlns:esg="https://example.org/esg" name="esg:GrossEmissions" contextRef="year" unitRef="emissions" decimals="0">1500</ix:nonFraction></div>
                """
            );

        EsefInlineXbrlParser.TryParse(html, out var facts).Should().Be(expected);
        EsefAnnualPeriod.IsProvenInline(html, Lei, End).Should().Be(expected);
        if (expected)
        {
            facts.Should().HaveCount(3);
            facts.Should().ContainSingle(fact => fact.Unit == unit).Which.Value.Should().Be(1500);
        }
        else
        {
            facts.Should().BeEmpty();
        }
    }

    private static string AnnualInline() =>
        $"""
            <html xmlns="http://www.w3.org/1999/xhtml"
              xmlns:ix="http://www.xbrl.org/2013/inlineXBRL"
              xmlns:xbrli="http://www.xbrl.org/2003/instance"
              xmlns:ifrs-full="http://xbrl.ifrs.org/taxonomy/2025/ifrs-full"
              xmlns:iso4217="http://www.xbrl.org/2003/iso4217">
            <head><title>Annual report</title></head><body>
            <ix:header><ix:resources>
              <xbrli:context id="year"><xbrli:entity><xbrli:identifier scheme="http://standards.iso.org/iso/17442">{Lei}</xbrli:identifier></xbrli:entity>
                <xbrli:period><xbrli:startDate>2025-01-01</xbrli:startDate><xbrli:endDate>2025-12-31</xbrli:endDate></xbrli:period></xbrli:context>
              <xbrli:context id="point"><xbrli:entity><xbrli:identifier scheme="http://standards.iso.org/iso/17442">{Lei}</xbrli:identifier></xbrli:entity>
                <xbrli:period><xbrli:instant>2025-12-31</xbrli:instant></xbrli:period></xbrli:context>
              <xbrli:unit id="dkk"><xbrli:measure>iso4217:DKK</xbrli:measure></xbrli:unit>
            </ix:resources></ix:header>
            <div><ix:nonFraction name="ifrs-full:ProfitLoss" contextRef="year" unitRef="dkk" decimals="0">100</ix:nonFraction>
            <ix:nonFraction name="ifrs-full:Assets" contextRef="point" unitRef="dkk" decimals="0">200</ix:nonFraction></div>
            </body></html>
            """;

    private static ParsedXbrlFact Fact(
        bool instant,
        int days = 364,
        string issuer = Lei,
        string taxonomy = "http://xbrl.ifrs.org/taxonomy/2025/ifrs-full",
        decimal value = 1
    ) =>
        new()
        {
            ConsolidatedLei = issuer,
            Namespace = taxonomy,
            Tag = instant ? "Assets" : "ProfitLoss",
            Unit = "DKK",
            PeriodStart = instant ? End : End.AddDays(-days),
            PeriodEnd = End,
            IsInstant = instant,
            Value = value,
        };
}
