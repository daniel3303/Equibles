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
