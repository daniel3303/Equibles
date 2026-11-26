using Equibles.CommonStocks.Data.Models;
using Equibles.Sec.Data.Models;

namespace Equibles.UnitTests.Sec;

public class EdgarSubmissionAddressTests
{
    private const string Accession = "0001654954-23-004613";

    // The issuer's own CIK is the answer whenever the stored address does not name this filing
    // under another one, so every fetch outside that case asks exactly as before.
    [Theory]
    // The same CIK, written with and without leading zeros on either side.
    [InlineData(
        "2152623",
        "https://www.sec.gov/Archives/edgar/data/2152623/0001654954-23-004613.txt"
    )]
    [InlineData(
        "2152623",
        "https://www.sec.gov/Archives/edgar/data/0002152623/0001654954-23-004613.txt"
    )]
    [InlineData(
        "0002152623",
        "https://www.sec.gov/Archives/edgar/data/2152623/0001654954-23-004613.txt"
    )]
    // An address of another filing, of no CIK, of another archive, or none at all.
    [InlineData(
        "2152623",
        "https://www.sec.gov/Archives/edgar/data/0001593538/0001654954-23-000001.txt"
    )]
    [InlineData(
        "2152623",
        "https://www.sec.gov/Archives/edgar/data/0000000000/0001654954-23-004613.txt"
    )]
    [InlineData("2152623", "https://filings.xbrl.org/529900/2025-12-31/ESEF/BE/0/report.json")]
    [InlineData("2152623", null)]
    [InlineData(null, null)]
    public void Cik_AddressThatNamesNoOtherCikForTheFiling_IsTheIssuersOwn(
        string issuerCik,
        string sourceUrl
    )
    {
        var document = Filing(issuerCik, sourceUrl);

        EdgarSubmissionAddress.Cik(document).Should().Be(issuerCik);
    }

    // A holding-company reorganization: the issuer's CIK is new, the 2023 filing stays under
    // the CIK that filed it.
    [Theory]
    [InlineData("https://www.sec.gov/Archives/edgar/data/0001593538/0001654954-23-004613.txt")]
    [InlineData("https://www.sec.gov/Archives/edgar/data/1593538/0001654954-23-004613.txt")]
    public void Cik_AddressThatNamesTheFilingUnderAnotherCik_IsThatCik(string sourceUrl)
    {
        var document = Filing("2152623", sourceUrl);

        EdgarSubmissionAddress.Cik(document).Should().Be("1593538");
    }

    // A legacy row stored before accessions were stamped: its address is its only identity.
    [Fact]
    public void Cik_FilingWithoutAStoredAccession_IsReadFromItsAddress()
    {
        var document = Filing(
            "2152623",
            "https://www.sec.gov/Archives/edgar/data/0001593538/0001654954-23-004613.txt"
        );
        document.AccessionNumber = null;

        EdgarSubmissionAddress.Cik(document).Should().Be("1593538");
    }

    // A caller that resolved the issuer by itself passes its CIK; the filing's issuer is not read.
    [Fact]
    public void Cik_IssuerCikGivenByTheCaller_IsJudgedAgainstTheAddress()
    {
        var document = Filing(
            null,
            "https://www.sec.gov/Archives/edgar/data/0001593538/0001654954-23-004613.txt"
        );
        document.Issuer = null;

        EdgarSubmissionAddress.Cik(document, "2152623").Should().Be("1593538");
        EdgarSubmissionAddress.Cik(document, "0001593538").Should().Be("0001593538");
    }

    [Fact]
    public void Cik_FilingWithoutAnIssuerCik_IsTheCikOfItsAddress()
    {
        var document = Filing(
            null,
            "https://www.sec.gov/Archives/edgar/data/0001593538/0001654954-23-004613.txt"
        );

        EdgarSubmissionAddress.Cik(document).Should().Be("1593538");
    }

    private static Document Filing(string issuerCik, string sourceUrl) =>
        new()
        {
            AccessionNumber = Accession,
            SourceUrl = sourceUrl,
            Issuer = new EquityIssuer { Cik = issuerCik },
        };
}
