using System.Globalization;
using System.Text;

namespace Equibles.Web.Services;

/// <summary>
/// Minimal RFC-4180 CSV writer. No external dependency; values that contain a comma,
/// double-quote, newline, or carriage return are wrapped in quotes and any inner
/// double-quotes are doubled. Numeric / DateOnly conversions use the invariant culture
/// so the output is stable across hosts regardless of the request thread's culture.
/// </summary>
public static class CsvExportService
{
    public static string BuildCsv(
        IReadOnlyList<string> headers,
        IEnumerable<IReadOnlyList<string>> rows
    )
    {
        var sb = new StringBuilder();
        AppendRow(sb, headers);
        foreach (var row in rows)
            AppendRow(sb, row);
        return sb.ToString();
    }

    public static string EscapeField(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;
        var needsQuoting = value.IndexOfAny(['"', ',', '\n', '\r']) >= 0;
        if (!needsQuoting)
            return value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    // Null renders as an empty CSV cell; otherwise the string is written verbatim.
    public static string Format(string value) => value ?? string.Empty;

    // Spreadsheet formula-injection lead-ins: a value beginning with one of these is treated as a
    // formula by Excel/Sheets on open. \t and \r are control chars some apps also treat as lead-ins.
    private static readonly char[] FormulaLeadIns = ['=', '+', '-', '@', '\t', '\r'];

    // Free-text cell formatter that neutralises spreadsheet formula injection: a value starting with
    // a formula lead-in is prefixed with a quote so crafted text (e.g. an institution name lifted
    // straight from a 13F filing, which anyone can file) can't execute as a formula when the CSV is
    // opened. Use for attacker-influenced text; numeric cells stay numeric and so go through the
    // Format(...) overloads, not this. RFC-4180 quoting is still applied afterwards by EscapeField.
    public static string FormatText(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;
        return FormulaLeadIns.Contains(value[0]) ? "'" + value : value;
    }

    public static string Format(long value) => value.ToString(CultureInfo.InvariantCulture);

    public static string Format(double value) =>
        value.ToString("G17", CultureInfo.InvariantCulture);

    public static string Format(decimal value) => value.ToString(CultureInfo.InvariantCulture);

    public static string Format(DateOnly date) =>
        date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static void AppendRow(StringBuilder sb, IReadOnlyList<string> fields)
    {
        for (var i = 0; i < fields.Count; i++)
        {
            if (i > 0)
                sb.Append(',');
            sb.Append(EscapeField(fields[i]));
        }
        sb.Append('\n');
    }
}
