using System.Globalization;
using Equibles.Integrations.Sec.Models;

namespace Equibles.Integrations.Sec.FormAdv;

/// <summary>
/// Parses the SEC's bulk Form ADV CSV (one adviser per row) into <see cref="FormAdvAdviserData"/>.
/// Columns are located by their header names rather than fixed positions, so the parser keeps
/// working when the SEC adds or reorders columns between monthly releases, and serves both the
/// registered and the exempt reporting adviser rosters (the exempt one has no Item 5). Rows
/// without a usable Organization CRD number are skipped, since an adviser cannot be keyed.
/// </summary>
public static class FormAdvCsvParser
{
    private const string CrdHeader = "Organization CRD#";
    private const string SecNumberHeader = "SEC#";
    private const string LegalNameHeader = "Legal Name";
    private const string PrimaryBusinessNameHeader = "Primary Business Name";
    private const string CityHeader = "Main Office City";
    private const string StateHeader = "Main Office State";
    private const string CountryHeader = "Main Office Country";
    private const string WebsiteHeader = "Website Address";
    private const string SecStatusHeader = "SEC Current Status";
    private const string EmployeesHeader = "5A";
    private const string DiscretionaryAumHeader = "5F(2)(a)";
    private const string NonDiscretionaryAumHeader = "5F(2)(b)";
    private const string TotalAumHeader = "5F(2)(c)";
    private const string ChargesPercentageOfAumHeader = "5E(1)";
    private const string ChargesHourlyHeader = "5E(2)";
    private const string ChargesSubscriptionHeader = "5E(3)";
    private const string ChargesFixedHeader = "5E(4)";
    private const string ChargesCommissionsHeader = "5E(5)";
    private const string ChargesPerformanceBasedHeader = "5E(6)";
    private const string ChargesOtherHeader = "5E(7)";
    private const string FirmTypeHeader = "Firm Type";
    private const string CikHeader = "CIK#";
    private const string CikCountHeader = "Total number of CIK numbers";
    private const string LeiHeader = "1P";
    private const string LatestFilingDateHeader = "Latest ADV Filing Date";
    private const string OrganizationFormHeader = "3A";
    private const string PrivateFundsHeader = "7B";
    private const string PrivateFundCountHeader = "Count of Private Funds - 7B(1)";
    private const string PrivateFundGrossAssetsHeader = "Total Gross Assets of Private Funds";
    private const string ClientTypeLetters = "abcdefghijklmn";

    public static IEnumerable<FormAdvAdviserData> Parse(TextReader reader)
    {
        using var records = CsvRecordReader.Read(reader).GetEnumerator();

        if (!records.MoveNext())
        {
            yield break;
        }

        var columns = BuildColumnMap(records.Current);

        while (records.MoveNext())
        {
            var adviser = MapRow(records.Current, columns);
            if (adviser != null)
            {
                yield return adviser;
            }
        }
    }

    private static Dictionary<string, int> BuildColumnMap(List<string> header)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < header.Count; i++)
        {
            var name = header[i].Trim();
            // Keep the first occurrence — the persisted columns are unique in the SEC layout.
            map.TryAdd(name, i);
        }

        return map;
    }

    private static FormAdvAdviserData MapRow(List<string> row, Dictionary<string, int> columns)
    {
        var crdText = Field(row, columns, CrdHeader);
        if (!int.TryParse(crdText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var crd))
        {
            return null;
        }

        return new FormAdvAdviserData
        {
            Crd = crd,
            SecNumber = Field(row, columns, SecNumberHeader),
            LegalName = Field(row, columns, LegalNameHeader),
            PrimaryBusinessName = Field(row, columns, PrimaryBusinessNameHeader),
            MainOfficeCity = Field(row, columns, CityHeader),
            MainOfficeState = Field(row, columns, StateHeader),
            MainOfficeCountry = Field(row, columns, CountryHeader),
            WebsiteAddress = Field(row, columns, WebsiteHeader),
            SecStatus = Field(row, columns, SecStatusHeader),
            NumberOfEmployees = ParseInt(Field(row, columns, EmployeesHeader)),
            DiscretionaryAum = ParseAum(Field(row, columns, DiscretionaryAumHeader)),
            NonDiscretionaryAum = ParseAum(Field(row, columns, NonDiscretionaryAumHeader)),
            TotalRegulatoryAum = ParseAum(Field(row, columns, TotalAumHeader)),
            ChargesPercentageOfAum = ParseYesNo(Field(row, columns, ChargesPercentageOfAumHeader)),
            ChargesHourly = ParseYesNo(Field(row, columns, ChargesHourlyHeader)),
            ChargesSubscription = ParseYesNo(Field(row, columns, ChargesSubscriptionHeader)),
            ChargesFixed = ParseYesNo(Field(row, columns, ChargesFixedHeader)),
            ChargesCommissions = ParseYesNo(Field(row, columns, ChargesCommissionsHeader)),
            ChargesPerformanceBased = ParseYesNo(
                Field(row, columns, ChargesPerformanceBasedHeader)
            ),
            ChargesOther = ParseYesNo(Field(row, columns, ChargesOtherHeader)),
            FirmType = Field(row, columns, FirmTypeHeader),
            Cik = Field(row, columns, CikHeader),
            CikCount = ParseInt(Field(row, columns, CikCountHeader)),
            Lei = Field(row, columns, LeiHeader),
            LatestFilingDate = ParseDate(Field(row, columns, LatestFilingDateHeader)),
            OrganizationForm = Field(row, columns, OrganizationFormHeader),
            RegistrationBasisItems = CheckedItems(row, columns, "2A", 13),
            ExemptionBasisItems = CheckedItems(row, columns, "2B", 3),
            OtherBusinessItems = CheckedItems(row, columns, "6A", 14),
            RelatedPersonItems = CheckedItems(row, columns, "7A", 16),
            AdvisesPrivateFunds = ParseOptionalYesNo(Field(row, columns, PrivateFundsHeader)),
            PrivateFundCount = ParseInt(Field(row, columns, PrivateFundCountHeader)),
            HedgeFundCount = PrivateFundTypeCount(row, columns, "Hedge"),
            PrivateEquityFundCount = PrivateFundTypeCount(row, columns, "PE"),
            VentureCapitalFundCount = PrivateFundTypeCount(row, columns, "VC"),
            RealEstateFundCount = PrivateFundTypeCount(row, columns, "Real Estate"),
            LiquidityFundCount = PrivateFundTypeCount(row, columns, "Liquidity"),
            SecuritizedAssetFundCount = PrivateFundTypeCount(row, columns, "Securitized"),
            OtherPrivateFundCount = PrivateFundTypeCount(row, columns, "Other"),
            PrivateFundGrossAssets = ParseAum(Field(row, columns, PrivateFundGrossAssetsHeader)),
            ClientTypes = ClientTypes(row, columns),
        };
    }

    // Item (n) of a check-all-that-apply list sets bit n-1, matching the Data enums' layout.
    private static int CheckedItems(
        List<string> row,
        Dictionary<string, int> columns,
        string item,
        int count
    )
    {
        var items = 0;
        for (var number = 1; number <= count; number++)
        {
            if (ParseYesNo(Field(row, columns, $"{item}({number})")))
                items |= 1 << (number - 1);
        }
        return items;
    }

    // A fund type the adviser answers "N" to has no count column value; that is a reported zero.
    private static int? PrivateFundTypeCount(
        List<string> row,
        Dictionary<string, int> columns,
        string fundType
    )
    {
        var count = ParseInt(Field(row, columns, $"Total number of {fundType} funds"));
        if (count.HasValue)
            return count;
        return ParseOptionalYesNo(Field(row, columns, $"Any {fundType} Funds")) == false ? 0 : null;
    }

    private static List<FormAdvClientTypeData> ClientTypes(
        List<string> row,
        Dictionary<string, int> columns
    )
    {
        var clientTypes = new List<FormAdvClientTypeData>();
        for (var index = 0; index < ClientTypeLetters.Length; index++)
        {
            var letter = ClientTypeLetters[index];
            // SEC writes 0 in (1) when "fewer than 5 clients" is checked instead of a count.
            var fewerThanFive = Field(row, columns, $"5D({letter})(2)") != null;
            var count = fewerThanFive ? null : ParseInt(Field(row, columns, $"5D({letter})(1)"));
            var aum = ParseAum(Field(row, columns, $"5D({letter})(3)"));
            if ((count is null or 0) && !fewerThanFive && (aum is null or 0))
                continue;
            clientTypes.Add(
                new FormAdvClientTypeData
                {
                    Index = index,
                    ClientCount = count,
                    FewerThanFiveClients = fewerThanFive,
                    RegulatoryAum = aum,
                }
            );
        }
        return clientTypes;
    }

    /// <summary>Returns the trimmed value for a column, or null when the column is absent or blank.</summary>
    private static string Field(List<string> row, Dictionary<string, int> columns, string header)
    {
        if (!columns.TryGetValue(header, out var index) || index >= row.Count)
        {
            return null;
        }

        var value = row[index].Trim();
        return value.Length == 0 ? null : value;
    }

    private static int? ParseInt(string value)
    {
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
        {
            return result;
        }

        return null;
    }

    /// <summary>
    /// Parses an assets-under-management figure such as "2,481,367,832.00" into whole dollars.
    /// Returns null for a blank cell so "not reported" is distinguishable from a reported zero.
    /// </summary>
    private static long? ParseAum(string value)
    {
        if (
            decimal.TryParse(
                value,
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out var amount
            )
        )
        {
            var rounded = Math.Round(amount, MidpointRounding.AwayFromZero);

            // A figure with more digits than long can hold is still a valid decimal, so the
            // narrowing cast would overflow and abort the whole streaming import. Treat an
            // out-of-range AUM as "not reported" (null), consistent with a blank cell.
            if (rounded < long.MinValue || rounded > long.MaxValue)
                return null;

            return (long)rounded;
        }

        return null;
    }

    private static bool ParseYesNo(string value) =>
        string.Equals(value, "Y", StringComparison.OrdinalIgnoreCase);

    private static bool? ParseOptionalYesNo(string value) =>
        value == null ? null : ParseYesNo(value);

    private static DateOnly? ParseDate(string value) =>
        DateOnly.TryParseExact(
            value,
            "MM/dd/yyyy",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var date
        )
            ? date
            : null;
}
