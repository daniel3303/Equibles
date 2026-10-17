using System.Globalization;
using System.IO.Compression;
using System.Text;
using Equibles.Sec.Data.Models;
using Equibles.Sec.HostedService.Models;

namespace Equibles.Sec.HostedService.Helpers;

/// <summary>
/// Parses SEC's quarterly Form N-CEN data set archive into one report per N-CEN or N-CEN/A
/// accession, each with its funds, advisers, share classes and exchange listings. Only the six
/// tables those need are read; the rest of the archive (including the large CREDIT_USER table) is
/// never opened. Notices of late filing carry no report and are skipped.
/// </summary>
public static class NCenDataSetParser
{
    private static readonly string[] DateFormats = ["dd-MMM-yyyy"];

    private static readonly (string Column, NCenFundTypes Type)[] FundTypeColumns =
    [
        ("IS_ETF", NCenFundTypes.ExchangeTradedFund),
        ("IS_ETMF", NCenFundTypes.ExchangeTradedManagedFund),
        ("IS_INDEX", NCenFundTypes.IndexFund),
        ("IS_MULTI_INVERSE_INDEX", NCenFundTypes.MultipleOrInverseIndexFund),
        ("IS_INTERVAL", NCenFundTypes.IntervalFund),
        ("IS_FUND_OF_FUND", NCenFundTypes.FundOfFunds),
        ("IS_MASTER_FEEDER", NCenFundTypes.MasterFeederFund),
        ("IS_MONEY_MARKET", NCenFundTypes.MoneyMarketFund),
        ("IS_TARGET_DATE", NCenFundTypes.TargetDateFund),
        ("IS_UNDERLYING_FUND", NCenFundTypes.UnderlyingFund),
    ];

    public static List<NCenRegistrantReportData> Parse(ZipArchive archive)
    {
        var submissions = Table(archive, "SUBMISSION");
        submissions.Require(
            "ACCESSION_NUMBER",
            "SUBMISSION_TYPE",
            "CIK",
            "FILING_DATE",
            "REPORT_ENDING_PERIOD"
        );
        var registrants = Table(archive, "REGISTRANT");
        registrants.Require("ACCESSION_NUMBER", "REGISTRANT_NAME");
        var funds = Table(archive, "FUND_REPORTED_INFO");
        funds.Require("FUND_ID", "ACCESSION_NUMBER", "SERIES_ID");
        funds.Require(FundTypeColumns.Select(column => column.Column).ToArray());
        var advisers = Table(archive, "ADVISER");
        advisers.Require("FUND_ID", "ADVISER_TYPE", "ADVISER_NAME", "CRD_NUM");
        var classes = Table(archive, "SHARES_OUTSTANDING");
        classes.Require("FUND_ID", "CLASS_ID", "TICKER");
        var listings = Table(archive, "SECURITY_EXCHANGE");
        listings.Require("FUND_ID", "FUND_EXCHANGE", "FUND_TICKER_SYMBOL");

        var registrantByAccession = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var row in registrants.Rows)
            registrantByAccession.TryAdd(registrants.Value(row, "ACCESSION_NUMBER") ?? "", row);

        var reports = new Dictionary<string, NCenRegistrantReportData>(StringComparer.Ordinal);
        foreach (var row in submissions.Rows)
        {
            var type = submissions.Value(row, "SUBMISSION_TYPE");
            if (type is not ("N-CEN" or "N-CEN/A"))
                continue;
            var accession = submissions.Value(row, "ACCESSION_NUMBER");
            if (accession == null || reports.ContainsKey(accession))
                throw new InvalidDataException(
                    $"N-CEN data set SUBMISSION repeats or omits accession number '{accession}'."
                );
            registrantByAccession.TryGetValue(accession, out var registrant);
            reports[accession] = new NCenRegistrantReportData
            {
                AccessionNumber = accession,
                Cik = submissions.Value(row, "CIK"),
                IsAmendment = type == "N-CEN/A",
                FilingDate = RequiredDate(submissions.Value(row, "FILING_DATE"), accession),
                ReportEndingPeriod = RequiredDate(
                    submissions.Value(row, "REPORT_ENDING_PERIOD"),
                    accession
                ),
                RegistrantName = Text(registrants, registrant, "REGISTRANT_NAME"),
                InvestmentCompanyFileNumber = Text(registrants, registrant, "FILE_NUM"),
                Lei = Text(registrants, registrant, "LEI"),
                InvestmentCompanyType = Text(registrants, registrant, "INVESTMENT_COMPANY_TYPE"),
                IsFamilyInvestmentCompany = YesNo(
                    Text(registrants, registrant, "IS_FAMILY_INVESTMENT_COMPANY")
                ),
                FamilyInvestmentCompanyName = Text(
                    registrants,
                    registrant,
                    "FAMILY_INVESTMENT_COMPANY_NAME"
                ),
                TotalSeries = Integer(Text(registrants, registrant, "TOTAL_SERIES")),
            };
        }

        var fundById = new Dictionary<string, NCenFundData>(StringComparer.Ordinal);
        foreach (var row in funds.Rows)
        {
            var fundId = funds.Value(row, "FUND_ID");
            var accession = funds.Value(row, "ACCESSION_NUMBER");
            if (fundId == null || fundById.ContainsKey(fundId))
                throw new InvalidDataException(
                    $"N-CEN data set FUND_REPORTED_INFO repeats or omits fund id '{fundId}'."
                );
            if (accession == null || !reports.TryGetValue(accession, out var report))
                continue;
            var fund = new NCenFundData
            {
                FundId = fundId,
                SeriesId = Text(funds, row, "SERIES_ID"),
                FundName = Text(funds, row, "FUND_NAME"),
                Lei = Text(funds, row, "LEI"),
                IsIndexAffiliated = YesNo(funds.Value(row, "IS_INDEX_AFFILIATED")),
                IsIndexExclusive = YesNo(funds.Value(row, "IS_INDEX_EXCLUSIVE")),
                MonthlyAverageNetAssets = Amount(funds.Value(row, "MONTHLY_AVG_NET_ASSETS")),
                DailyAverageNetAssets = Amount(funds.Value(row, "DAILY_AVG_NET_ASSETS")),
            };
            foreach (var (column, fundType) in FundTypeColumns)
            {
                if (YesNo(funds.Value(row, column)) == true)
                    fund.FundTypes |= fundType;
            }
            fundById[fundId] = fund;
            report.Funds.Add(fund);
        }

        foreach (var row in advisers.Rows)
        {
            if (!fundById.TryGetValue(advisers.Value(row, "FUND_ID") ?? "", out var fund))
                continue;
            fund.Advisers.Add(
                new NCenAdviserData
                {
                    Role = Role(advisers.Value(row, "ADVISER_TYPE")),
                    Name = Text(advisers, row, "ADVISER_NAME"),
                    FileNumber = Text(advisers, row, "FILE_NUM"),
                    Crd = Crd(advisers.Value(row, "CRD_NUM")),
                    Lei = Text(advisers, row, "ADVISER_LEI"),
                    State = Text(advisers, row, "STATE"),
                    Country = Text(advisers, row, "COUNTRY"),
                    IsAffiliated = YesNo(advisers.Value(row, "IS_AFFILIATED")),
                    IsHiredDuringPeriod = YesNo(advisers.Value(row, "IS_ADVISOR_HIRED")),
                    StartDate = Date(advisers.Value(row, "ADVISOR_START_DATE")),
                    TerminationDate = Date(advisers.Value(row, "ADVISOR_TERMINATED_DATE")),
                }
            );
        }

        foreach (var row in classes.Rows)
        {
            if (!fundById.TryGetValue(classes.Value(row, "FUND_ID") ?? "", out var fund))
                continue;
            fund.ShareClasses.Add(
                new NCenShareClassData
                {
                    ClassId = Text(classes, row, "CLASS_ID"),
                    ClassName = Text(classes, row, "CLASS_NAME"),
                    Ticker = Text(classes, row, "TICKER"),
                }
            );
        }

        foreach (var row in listings.Rows)
        {
            if (!fundById.TryGetValue(listings.Value(row, "FUND_ID") ?? "", out var fund))
                continue;
            var ticker = Text(listings, row, "FUND_TICKER_SYMBOL");
            var exchange = Text(listings, row, "FUND_EXCHANGE");
            if (ticker == null && exchange == null)
                continue;
            fund.ExchangeListings.Add(
                new NCenExchangeListingData { Exchange = exchange, Ticker = ticker }
            );
        }

        return reports.Values.ToList();
    }

    private static NCenTsvTable Table(ZipArchive archive, string name)
    {
        var entry =
            archive.Entries.FirstOrDefault(e =>
                string.Equals(e.Name, name + ".tsv", StringComparison.OrdinalIgnoreCase)
            ) ?? throw new InvalidDataException($"N-CEN data set has no {name}.tsv table.");
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        return NCenTsvTable.Read(name, reader);
    }

    // SEC writes "N/A" in unused text slots; that is absence, never a value.
    private static string Text(NCenTsvTable table, string[] row, string column)
    {
        if (row == null)
            return null;
        var value = table.Value(row, column);
        return value == null || value.Equals("N/A", StringComparison.OrdinalIgnoreCase)
            ? null
            : value;
    }

    internal static bool? YesNo(string value) =>
        value?.ToUpperInvariant() switch
        {
            "Y" => true,
            "N" => false,
            _ => null,
        };

    internal static DateOnly? Date(string value) =>
        DateOnly.TryParseExact(
            value,
            DateFormats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var date
        )
            ? date
            : null;

    private static DateOnly RequiredDate(string value, string accession) =>
        Date(value)
        ?? throw new InvalidDataException(
            $"N-CEN data set accession {accession} has an unreadable date '{value}'."
        );

    private static decimal? Amount(string value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount)
            ? amount
            : null;

    private static int? Integer(string value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
            ? number
            : null;

    // CRD numbers arrive zero-padded ("000105790"); zero and non-numeric placeholders are absence.
    internal static int? Crd(string value) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var crd) && crd > 0
            ? crd
            : null;

    // SEC spells the roles "Advisor", "Subadvisor", "Terminated Advisor" and "Terminated Subadvisor".
    internal static NCenAdviserRole Role(string value)
    {
        var normalized = value?.Replace(" ", "").ToUpperInvariant();
        return normalized switch
        {
            "ADVISOR" or "ADVISER" => NCenAdviserRole.InvestmentAdviser,
            "SUBADVISOR" or "SUBADVISER" => NCenAdviserRole.SubAdviser,
            "TERMINATEDADVISOR" or "TERMINATEDADVISER" =>
                NCenAdviserRole.TerminatedInvestmentAdviser,
            "TERMINATEDSUBADVISOR" or "TERMINATEDSUBADVISER" =>
                NCenAdviserRole.TerminatedSubAdviser,
            _ => throw new InvalidDataException(
                $"N-CEN data set ADVISER has an unknown adviser type '{value}'."
            ),
        };
    }
}
