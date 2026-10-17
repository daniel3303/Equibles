using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Equibles.Sec.Data.Models;
using Equibles.Sec.HostedService.Models;

namespace Equibles.Sec.HostedService.Helpers;

/// <summary>
/// Parses SEC's quarterly Form N-CEN data set archive into one report per N-CEN or N-CEN/A accession,
/// with its funds, advisers, share classes and exchange listings, from the six tables they need.
/// Every column read is required and every value must parse, so a format change fails loudly.
/// </summary>
public static partial class NCenDataSetParser
{
    private static readonly string[] DateFormats = ["dd-MMM-yyyy"];

    [GeneratedRegex(@"^\d{10}-\d{2}-\d{6}$", RegexOptions.CultureInvariant)]
    private static partial Regex AccessionPattern();

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
        registrants.Require(
            "ACCESSION_NUMBER",
            "REGISTRANT_NAME",
            "FILE_NUM",
            "LEI",
            "INVESTMENT_COMPANY_TYPE",
            "IS_FAMILY_INVESTMENT_COMPANY",
            "FAMILY_INVESTMENT_COMPANY_NAME",
            "TOTAL_SERIES"
        );
        var funds = Table(archive, "FUND_REPORTED_INFO");
        funds.Require(
            "FUND_ID",
            "ACCESSION_NUMBER",
            "SERIES_ID",
            "FUND_NAME",
            "LEI",
            "IS_INDEX_AFFILIATED",
            "IS_INDEX_EXCLUSIVE",
            "MONTHLY_AVG_NET_ASSETS",
            "DAILY_AVG_NET_ASSETS"
        );
        funds.Require(FundTypeColumns.Select(column => column.Column).ToArray());
        var advisers = Table(archive, "ADVISER");
        advisers.Require(
            "FUND_ID",
            "ADVISER_TYPE",
            "ADVISER_NAME",
            "FILE_NUM",
            "CRD_NUM",
            "ADVISER_LEI",
            "STATE",
            "COUNTRY",
            "IS_AFFILIATED",
            "IS_ADVISOR_HIRED",
            "ADVISOR_START_DATE",
            "ADVISOR_TERMINATED_DATE"
        );
        var classes = Table(archive, "SHARES_OUTSTANDING");
        classes.Require("FUND_ID", "CLASS_NAME", "CLASS_ID", "TICKER");
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
            if (
                accession == null
                || !AccessionPattern().IsMatch(accession)
                || reports.ContainsKey(accession)
            )
                throw new InvalidDataException(
                    $"N-CEN data set SUBMISSION has a repeated or malformed accession number '{accession}'."
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
                    Text(registrants, registrant, "IS_FAMILY_INVESTMENT_COMPANY"),
                    "REGISTRANT"
                ),
                FamilyInvestmentCompanyName = Text(
                    registrants,
                    registrant,
                    "FAMILY_INVESTMENT_COMPANY_NAME"
                ),
                TotalSeries = Integer(Text(registrants, registrant, "TOTAL_SERIES"), "REGISTRANT"),
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
                IsIndexAffiliated = YesNo(
                    funds.Value(row, "IS_INDEX_AFFILIATED"),
                    "FUND_REPORTED_INFO"
                ),
                IsIndexExclusive = YesNo(
                    funds.Value(row, "IS_INDEX_EXCLUSIVE"),
                    "FUND_REPORTED_INFO"
                ),
                MonthlyAverageNetAssets = Amount(
                    funds.Value(row, "MONTHLY_AVG_NET_ASSETS"),
                    "FUND_REPORTED_INFO"
                ),
                DailyAverageNetAssets = Amount(
                    funds.Value(row, "DAILY_AVG_NET_ASSETS"),
                    "FUND_REPORTED_INFO"
                ),
            };
            foreach (var (column, fundType) in FundTypeColumns)
            {
                if (YesNo(funds.Value(row, column), "FUND_REPORTED_INFO") == true)
                    fund.FundTypes |= fundType;
            }
            fundById[fundId] = fund;
            report.Funds.Add(fund);
        }

        // Rows of funds the report tables do not list are skipped.
        IEnumerable<(string[] Row, NCenFundData Fund)> OfKnownFunds(NCenTsvTable table) =>
            table
                .Rows.Select(row =>
                    (row, fundById.GetValueOrDefault(table.Value(row, "FUND_ID") ?? ""))
                )
                .Where(pair => pair.Item2 != null);

        foreach (var (row, fund) in OfKnownFunds(advisers))
        {
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
                    IsAffiliated = YesNo(advisers.Value(row, "IS_AFFILIATED"), "ADVISER"),
                    IsHiredDuringPeriod = YesNo(advisers.Value(row, "IS_ADVISOR_HIRED"), "ADVISER"),
                    StartDate = OptionalDate(advisers.Value(row, "ADVISOR_START_DATE"), "ADVISER"),
                    TerminationDate = OptionalDate(
                        advisers.Value(row, "ADVISOR_TERMINATED_DATE"),
                        "ADVISER"
                    ),
                }
            );
        }

        foreach (var (row, fund) in OfKnownFunds(classes))
        {
            fund.ShareClasses.Add(
                new NCenShareClassData
                {
                    ClassId = Text(classes, row, "CLASS_ID"),
                    ClassName = Text(classes, row, "CLASS_NAME"),
                    Ticker = Text(classes, row, "TICKER"),
                }
            );
        }

        var namedListings = OfKnownFunds(listings)
            .Select(pair =>
                (
                    pair.Fund,
                    Ticker: Text(listings, pair.Row, "FUND_TICKER_SYMBOL"),
                    Exchange: Text(listings, pair.Row, "FUND_EXCHANGE")
                )
            )
            .Where(listing => listing.Ticker != null || listing.Exchange != null);
        foreach (var (fund, ticker, exchange) in namedListings)
        {
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

    // Flags are "Y", "N" or blank; anything else means SEC changed the format.
    internal static bool? YesNo(string value, string table) =>
        value?.ToUpperInvariant() switch
        {
            null => null,
            "Y" => true,
            "N" => false,
            _ => throw new InvalidDataException(
                $"N-CEN data set {table} has an unrecognized flag value '{value}'."
            ),
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

    private static DateOnly? OptionalDate(string value, string table) =>
        value == null
            ? null
            : Date(value)
                ?? throw new InvalidDataException(
                    $"N-CEN data set {table} has an unreadable date '{value}'."
                );

    private static DateOnly RequiredDate(string value, string accession) =>
        Date(value)
        ?? throw new InvalidDataException(
            $"N-CEN data set accession {accession} has an unreadable date '{value}'."
        );

    private static decimal? Amount(string value, string table)
    {
        if (value == null)
            return null;
        return decimal.TryParse(
            value,
            NumberStyles.Number,
            CultureInfo.InvariantCulture,
            out var amount
        )
            ? amount
            : throw new InvalidDataException(
                $"N-CEN data set {table} has an unreadable amount '{value}'."
            );
    }

    private static int? Integer(string value, string table)
    {
        if (value == null)
            return null;
        return int.TryParse(
            value,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var number
        )
            ? number
            : throw new InvalidDataException(
                $"N-CEN data set {table} has an unreadable count '{value}'."
            );
    }

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
