using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

// Real filings whose persons state several type codes each: Morgan Stanley's 13G/A
// (0000895421-26-000285, filed under Rule 13d-1(b)) and AyurMaya Capital's 13D/A
// (0000902664-25-002531). The parser used to keep only each person's first code.
public class Filing13DGXmlParserTypeCodesTests
{
    private readonly Filing13DGXmlParser _sut = new();

    private static string Load(string name) =>
        File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "TestAssets", "Holdings13DG", name)
        );

    [Fact]
    public void ParseFiling_13G_KeepsEveryCodeTheRuleAndTheItem3Categories()
    {
        var result = _sut.ParseFiling(
            Load("sc13ga-morgan-stanley-x0202.xml"),
            accessionNumber: "0000895421-26-000285",
            cik: "0000895421",
            filingDate: new DateOnly(2026, 10, 9)
        );

        result.RuleDesignations.Should().Equal("Rule 13d-1(b)");
        result.FilingPersonTypeCodes.Should().Equal("BD", "IA", "HC");
        result
            .ReportingPersons.Select(p => (p.Name, p.Cik, string.Join(",", p.TypeCodes)))
            .Should()
            .Equal(
                ("Morgan Stanley", null, "HC,CO"),
                ("Morgan Stanley Smith Barney LLC", null, "BD,IA,CO")
            );
    }

    [Fact]
    public void ParseFiling_13D_KeepsEveryCodeOfEachPerson()
    {
        var result = _sut.ParseFiling(
            Load("sc13d-multiple-type-codes.xml"),
            accessionNumber: "0000902664-25-002531",
            cik: "0001975990",
            filingDate: new DateOnly(2025, 5, 23)
        );

        result.RuleDesignations.Should().BeEmpty();
        result.FilingPersonTypeCodes.Should().BeEmpty();
        result
            .ReportingPersons.Select(p =>
                (p.Cik, string.Join(",", p.TypeCodes), p.CitizenshipOrOrganization)
            )
            .Should()
            .Equal(("1975990", "IA,PN", "DE"), ("1548144", "IN,HC", "X1"));
    }

    [Fact]
    public void ParseFiling_CodesListedInOneElement_AreSplit()
    {
        var xml = Load("sc13d-multiple-type-codes.xml")
            .Replace(
                "<typeOfReportingPerson>IA</typeOfReportingPerson>",
                "<typeOfReportingPerson>ia, oo</typeOfReportingPerson>"
            );

        var result = _sut.ParseFiling(
            xml,
            "0000902664-25-002531",
            "0001975990",
            new DateOnly(2025, 5, 23)
        );

        result.ReportingPersons[0].TypeCodes.Should().Equal("IA", "OO", "PN");
    }
}
