using System.IO.Compression;
using System.Text;
using Equibles.Sec.Data.Models;
using Equibles.Sec.HostedService.Helpers;
using Equibles.Sec.HostedService.Models;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// The N-CEN data set parser, over real rows sliced from SEC's 2026 Q3 and 2018 Q3 archives: fund
/// types, index details, net assets, adviser roles and CRD numbers, tickers, amendments, and the
/// refusals that keep a format change from importing silently.
/// </summary>
public class NCenDataSetParserTests
{
    private static readonly string[] Tables =
    [
        "SUBMISSION",
        "REGISTRANT",
        "FUND_REPORTED_INFO",
        "ADVISER",
        "SHARES_OUTSTANDING",
        "SECURITY_EXCHANGE",
    ];

    [Fact]
    public void Parse_2026Q3_KeepsEveryAnnualReport()
    {
        var reports = Parse("2026q3");

        reports.Should().HaveCount(6);
        reports
            .Single(r => r.AccessionNumber == "0001193125-26-388800")
            .IsAmendment.Should()
            .BeTrue();
        reports
            .Single(r => r.AccessionNumber == "0001193125-26-386137")
            .IsAmendment.Should()
            .BeFalse();
    }

    [Fact]
    public void Parse_IndexEtf_ReadsTypesIndexDetailsAdviserAndListing()
    {
        var report = Parse("2026q3").Single(r => r.AccessionNumber == "0001004726-26-005736");

        report.Cik.Should().Be("0000930667");
        report.RegistrantName.Should().Be("iShares, Inc.");
        report.InvestmentCompanyFileNumber.Should().Be("811-09102");
        report.InvestmentCompanyType.Should().Be("N-1A");
        report.IsFamilyInvestmentCompany.Should().BeTrue();
        report.FamilyInvestmentCompanyName.Should().Be("BlackRock-advised Funds");
        report.TotalSeries.Should().Be(53);
        report.FilingDate.Should().Be(new DateOnly(2026, 7, 14));
        report.ReportEndingPeriod.Should().Be(new DateOnly(2026, 4, 30));

        var fund = report.Funds.Single(f => f.SeriesId == "S000035998");
        fund.FundName.Should().Be("iShares Asia/Pacific Dividend ETF");
        fund.FundTypes.Should().Be(NCenFundTypes.ExchangeTradedFund | NCenFundTypes.IndexFund);
        fund.IsIndexAffiliated.Should().BeFalse();
        fund.IsIndexExclusive.Should().BeFalse();
        fund.MonthlyAverageNetAssets.Should().Be(53387539.92m);
        var adviser = fund.Advisers.Single();
        adviser.Role.Should().Be(NCenAdviserRole.InvestmentAdviser);
        adviser.Name.Should().Be("BlackRock Fund Advisors");
        adviser.Crd.Should().Be(105247);
        adviser.FileNumber.Should().Be("801-22609");
        adviser.Lei.Should().Be("549300YOOGP0Y1M95C20");
        adviser.State.Should().Be("US-CA");
        adviser.Country.Should().Be("US");
        adviser.IsHiredDuringPeriod.Should().BeFalse();
        fund.ExchangeListings.Should()
            .ContainSingle()
            .Which.Should()
            .BeEquivalentTo(new NCenExchangeListingData { Exchange = "ARCX", Ticker = "DVYA" });
    }

    [Fact]
    public void Parse_MoneyMarketFund_ReadsDailyNetAssetsAndClasses()
    {
        var fund = Parse("2026q3")
            .Single(r => r.AccessionNumber == "0000856517-26-000004")
            .Funds.Single();

        fund.FundTypes.Should().Be(NCenFundTypes.MoneyMarketFund);
        fund.DailyAverageNetAssets.Should().Be(67360568840.1863m);
        fund.MonthlyAverageNetAssets.Should().BeNull();
        fund.Advisers.Select(a => a.Crd).Should().BeEquivalentTo([128611, 105138]);
        fund.ShareClasses.Should().Contain(c => c.ClassId == "C000242773" && c.Ticker == "UTDXX");
    }

    [Fact]
    public void Parse_SubAdvisersHiredAndTerminated_KeepTheirRolesAndDates()
    {
        var fund = Parse("2026q3")
            .Single(r => r.AccessionNumber == "0000910472-26-011642")
            .Funds.Single(f => f.SeriesId == "S000029069");

        fund.FundTypes.Should().Be(NCenFundTypes.None);
        fund.Advisers.Count(a => a.Role == NCenAdviserRole.InvestmentAdviser).Should().Be(1);
        fund.Advisers.Count(a => a.Role == NCenAdviserRole.SubAdviser).Should().Be(3);
        fund.Advisers.Count(a => a.Role == NCenAdviserRole.TerminatedSubAdviser).Should().Be(2);
        var hired = fund.Advisers.Single(a =>
            a.Role == NCenAdviserRole.SubAdviser && a.Crd == 106220
        );
        hired.IsHiredDuringPeriod.Should().BeTrue();
        hired.StartDate.Should().Be(new DateOnly(2026, 5, 29));
        hired.IsAffiliated.Should().BeFalse();
        hired.Lei.Should().BeNull("SEC writes N/A for an adviser without an LEI");
        fund.Advisers.Single(a => a.Role == NCenAdviserRole.TerminatedSubAdviser && a.Crd == 147067)
            .TerminationDate.Should()
            .Be(new DateOnly(2026, 5, 29));
    }

    [Fact]
    public void Parse_RegistrantWithoutSeries_KeepsItsFundWithNoSeriesId()
    {
        var report = Parse("2026q3").Single(r => r.AccessionNumber == "0000940400-26-026182");

        report.InvestmentCompanyType.Should().Be("N-2");
        report.FamilyInvestmentCompanyName.Should().Be("The Pioneer Funds");
        report.Funds.Should().ContainSingle().Which.SeriesId.Should().BeNull();
    }

    [Fact]
    public void Parse_2018Q3_ReadsTheOldestVintage()
    {
        var reports = Parse("2018q3");

        reports.Should().HaveCount(3);
        var putnam = reports.Single(r => r.AccessionNumber == "0000869392-18-000195");
        var fund = putnam.Funds.Single();
        fund.SeriesId.Should().Be("S000005589");
        fund.MonthlyAverageNetAssets.Should().Be(218198819.85m);
        fund.Advisers.Should().OnlyContain(a => a.Crd == null, "the 2018 report wrote N/A for CRD");
        var london = fund.Advisers.Single(a => a.Name == "Putnam Investments Limited");
        london.Country.Should().Be("GB");
        london.State.Should().BeNull();
        london.IsAffiliated.Should().BeTrue();
        london.FileNumber.Should().Be("801-61441");

        var oppenheimer = reports.Single(r => r.AccessionNumber == "0001752724-18-000167");
        oppenheimer.Funds.Single().SeriesId.Should().BeNull();
        oppenheimer
            .Funds.Single()
            .ShareClasses.Should()
            .Contain(c => c.ClassId == null && c.Ticker == "OPMYX");
    }

    [Fact]
    public void Parse_LateFilingNotice_IsSkipped()
    {
        var reports = Parse(
            "2026q3",
            (
                "SUBMISSION",
                "0009999999-26-000001\tNT N-CEN\t0000999999\t01-JUL-2026\t31-MAR-2026"
                    + new string('\t', 17)
            )
        );

        reports.Should().NotContain(r => r.AccessionNumber == "0009999999-26-000001");
    }

    [Fact]
    public void Parse_UnknownAdviserType_IsRefused()
    {
        var act = () =>
            Parse(
                "2026q3",
                (
                    "ADVISER",
                    "0000856517-26-000004_0000856517_S000009580\tPart C\tCo-Advisor\tX\t801-1\t000000001\tN/A\tUS-PA\tUS\t\tN\t\t"
                )
            );

        act.Should().Throw<InvalidDataException>().WithMessage("*Co-Advisor*");
    }

    [Fact]
    public void Parse_RowWithTheWrongFieldCount_IsRefused()
    {
        var act = () => Parse("2026q3", ("SECURITY_EXCHANGE", "only-one-field"));

        act.Should().Throw<InvalidDataException>().WithMessage("*SECURITY_EXCHANGE line*");
    }

    [Fact]
    public void Parse_MissingTable_IsRefused()
    {
        var act = () => Parse("2026q3", omit: "ADVISER");

        act.Should().Throw<InvalidDataException>().WithMessage("*ADVISER.tsv*");
    }

    [Fact]
    public void Parse_MissingRequiredColumn_IsRefused()
    {
        var act = () => Parse("2026q3", renameHeader: ("FUND_REPORTED_INFO", "IS_INDEX", "IS_IDX"));

        act.Should().Throw<InvalidDataException>().WithMessage("*IS_INDEX*");
    }

    [Theory]
    [InlineData("IS_ETF", NCenFundTypes.ExchangeTradedFund)]
    [InlineData("IS_ETMF", NCenFundTypes.ExchangeTradedManagedFund)]
    [InlineData("IS_INDEX", NCenFundTypes.IndexFund)]
    [InlineData("IS_MULTI_INVERSE_INDEX", NCenFundTypes.MultipleOrInverseIndexFund)]
    [InlineData("IS_INTERVAL", NCenFundTypes.IntervalFund)]
    [InlineData("IS_FUND_OF_FUND", NCenFundTypes.FundOfFunds)]
    [InlineData("IS_MASTER_FEEDER", NCenFundTypes.MasterFeederFund)]
    [InlineData("IS_MONEY_MARKET", NCenFundTypes.MoneyMarketFund)]
    [InlineData("IS_TARGET_DATE", NCenFundTypes.TargetDateFund)]
    [InlineData("IS_UNDERLYING_FUND", NCenFundTypes.UnderlyingFund)]
    public void Parse_EachFundTypeColumn_SetsItsOwnFlag(string column, NCenFundTypes expected)
    {
        // The Valued Advisers fund reported no type; marking one column must yield only its flag.
        var fund = Parse(
                "2026q3",
                rewrite: (table, header, fields) =>
                    Set(table, header, fields, "FUND_REPORTED_INFO", "S000042102", column, "Y")
            )
            .SelectMany(r => r.Funds)
            .Single(f => f.SeriesId == "S000042102");

        fund.FundTypes.Should().Be(expected);
    }

    [Fact]
    public void Parse_FlagWrittenAsTrue_IsRefused()
    {
        var act = () =>
            Parse(
                "2026q3",
                rewrite: (table, header, fields) =>
                    Set(table, header, fields, "FUND_REPORTED_INFO", "S000042102", "IS_ETF", "true")
            );

        act.Should().Throw<InvalidDataException>().WithMessage("*flag value 'true'*");
    }

    [Fact]
    public void Parse_UnreadableAdviserDate_IsRefused()
    {
        var act = () =>
            Parse(
                "2026q3",
                rewrite: (table, header, fields) =>
                    Set(
                        table,
                        header,
                        fields,
                        "ADVISER",
                        "S000029069",
                        "ADVISOR_START_DATE",
                        "2026-05-29"
                    )
            );

        act.Should().Throw<InvalidDataException>().WithMessage("*unreadable date '2026-05-29'*");
    }

    [Fact]
    public void Parse_MissingNetAssetsColumn_IsRefused()
    {
        var act = () =>
            Parse(
                "2026q3",
                renameHeader: ("FUND_REPORTED_INFO", "MONTHLY_AVG_NET_ASSETS", "MONTHLY_NET_ASSETS")
            );

        act.Should().Throw<InvalidDataException>().WithMessage("*MONTHLY_AVG_NET_ASSETS*");
    }

    [Fact]
    public void Parse_MalformedAccessionNumber_IsRefused()
    {
        var act = () =>
            Parse(
                "2026q3",
                rewrite: (table, header, fields) =>
                    table == "SUBMISSION" && fields[0] == "0000856517-26-000004"
                        ? ["0000856517-26-0000040", .. fields[1..]]
                        : fields
            );

        act.Should().Throw<InvalidDataException>().WithMessage("*malformed accession*");
    }

    [Theory]
    [InlineData("29-JUL-2026", 2026, 7, 29)]
    [InlineData("01-Mar-2022", 2022, 3, 1)]
    public void Date_ReadsSecsDayMonthYear(string value, int year, int month, int day)
    {
        NCenDataSetParser.Date(value).Should().Be(new DateOnly(year, month, day));
    }

    [Theory]
    [InlineData("000105790", 105790)]
    [InlineData("N/A", null)]
    [InlineData("000000000", null)]
    [InlineData("", null)]
    public void Crd_DropsPaddingAndPlaceholders(string value, int? expected)
    {
        NCenDataSetParser.Crd(value).Should().Be(expected);
    }

    internal static List<NCenRegistrantReportData> Parse(
        string vintage,
        (string Table, string Row)? extraRow = null,
        string omit = null,
        (string Table, string From, string To)? renameHeader = null,
        Func<string, string[], string[], string[]> rewrite = null
    )
    {
        using var zip = BuildArchive(vintage, extraRow, omit, renameHeader, rewrite);
        using var archive = new ZipArchive(zip, ZipArchiveMode.Read);
        return NCenDataSetParser.Parse(archive);
    }

    // rewrite(table, header, fields) may return changed fields for a data row.
    internal static MemoryStream BuildArchive(
        string vintage,
        (string Table, string Row)? extraRow = null,
        string omit = null,
        (string Table, string From, string To)? renameHeader = null,
        Func<string, string[], string[], string[]> rewrite = null
    )
    {
        var zip = new MemoryStream();
        using (var archive = new ZipArchive(zip, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var table in Tables.Where(table => table != omit))
            {
                var text = File.ReadAllText(
                    Path.Combine(
                        AppContext.BaseDirectory,
                        "TestAssets",
                        "NCen",
                        vintage,
                        table + ".tsv"
                    )
                );
                if (rewrite != null)
                {
                    var lines = text.TrimEnd('\n').Split('\n');
                    var header = lines[0].Split('\t');
                    text =
                        string.Join(
                            "\n",
                            lines
                                .Skip(1)
                                .Select(line =>
                                    string.Join("\t", rewrite(table, header, line.Split('\t')))
                                )
                                .Prepend(lines[0])
                        ) + "\n";
                }
                if (renameHeader is { } rename && rename.Table == table)
                    text = text.Replace("\t" + rename.From + "\t", "\t" + rename.To + "\t");
                if (extraRow is { } extra && extra.Table == table)
                    text = text.TrimEnd('\n') + "\n" + extra.Row + "\n";
                var entry = archive.CreateEntry(table + ".tsv");
                using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
                writer.Write(text);
            }
        }
        zip.Position = 0;
        return zip;
    }

    // Sets one column on the rows of a table whose FUND_ID ends with the given series.
    private static string[] Set(
        string table,
        string[] header,
        string[] fields,
        string targetTable,
        string series,
        string column,
        string value
    )
    {
        if (table != targetTable || !fields[Array.IndexOf(header, "FUND_ID")].EndsWith(series))
            return fields;
        var copy = (string[])fields.Clone();
        copy[Array.IndexOf(header, column)] = value;
        return copy;
    }
}
