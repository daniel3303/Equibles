using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

// Record-replay against a real X0202-schema Schedule 13G (Avoro Capital's 2026-03-16
// 13G on AN2 Therapeutics). EDGAR's X0202 schema (effective ~2026-03-16) moved the
// issuer CUSIP from a single <issuerCusip> element to a wrapped list:
// <issuerCusips><issuerCusipNumber>…</issuerCusipNumber></issuerCusips>. A parser
// that only probes the old names returns a null CUSIP, and the import then maps the
// filing to no tracked stock — every post-X0202 13D/13G silently imports zero rows.
public class Filing13DGXmlParserX0202IssuerCusipsTests
{
    private readonly Filing13DGXmlParser _sut = new();

    [Fact]
    public void ParseFiling_X0202SchemaWithWrappedCusipList_ExtractsTheIssuerCusip()
    {
        var xml = File.ReadAllText(
            Path.Combine(
                AppContext.BaseDirectory,
                "TestAssets",
                "Holdings13DG",
                "sc13g-x0202-an2.xml"
            )
        );

        var result = _sut.ParseFiling(
            xml,
            accessionNumber: "0001831942-26-000014",
            cik: "0001831942",
            filingDate: new DateOnly(2026, 3, 16)
        );

        result.IssuerCusip.Should().Be("037326105");
        result.IssuerCik.Should().Be("1880438");
        result.IssuerName.Should().Be("AN2 Therapeutics, Inc.");
    }
}
