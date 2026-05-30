using System.Globalization;
using Equibles.Integrations.Sec.Models;

namespace Equibles.Integrations.Sec.FormAdv;

/// <summary>
/// Parses the SEC's bulk Form ADV CSV (one adviser per row) into <see cref="FormAdvAdviserData"/>.
/// Columns are located by their header names rather than fixed positions, so the parser keeps
/// working when the SEC adds or reorders columns between monthly releases (the file carries 260+
/// columns; only a handful are persisted). Rows without a usable Organization CRD number are
/// skipped — without it an adviser cannot be keyed.
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
            ChargesPercentageOfAum = ParseYesNo(Field(row, columns, "5E(1)")),
            ChargesHourly = ParseYesNo(Field(row, columns, "5E(2)")),
            ChargesSubscription = ParseYesNo(Field(row, columns, "5E(3)")),
            ChargesFixed = ParseYesNo(Field(row, columns, "5E(4)")),
            ChargesCommissions = ParseYesNo(Field(row, columns, "5E(5)")),
            ChargesPerformanceBased = ParseYesNo(Field(row, columns, "5E(6)")),
            ChargesOther = ParseYesNo(Field(row, columns, "5E(7)")),
        };
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
            return (long)Math.Round(amount, MidpointRounding.AwayFromZero);
        }

        return null;
    }

    private static bool ParseYesNo(string value) =>
        string.Equals(value, "Y", StringComparison.OrdinalIgnoreCase);
}
