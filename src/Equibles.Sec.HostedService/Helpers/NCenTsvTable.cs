namespace Equibles.Sec.HostedService.Helpers;

/// <summary>
/// Reads one tab-separated table of an N-CEN data set by column name. SEC writes these files
/// unquoted, so a row whose field count differs from the header is malformed and refused.
/// </summary>
internal sealed class NCenTsvTable
{
    private readonly string _name;
    private readonly Dictionary<string, int> _columns;

    private NCenTsvTable(string name, Dictionary<string, int> columns, List<string[]> rows)
    {
        _name = name;
        _columns = columns;
        Rows = rows;
    }

    internal List<string[]> Rows { get; }

    internal static NCenTsvTable Read(string name, TextReader reader)
    {
        var header = reader.ReadLine()?.TrimEnd('\r');
        if (string.IsNullOrEmpty(header))
            throw new InvalidDataException($"N-CEN data set table {name} has no header.");
        var names = header.Split('\t');
        var columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < names.Length; index++)
            columns.TryAdd(names[index].Trim(), index);

        var rows = new List<string[]>();
        var lineNumber = 1;
        string line;
        while ((line = reader.ReadLine()) != null)
        {
            lineNumber++;
            line = line.TrimEnd('\r');
            if (line.Length == 0)
                continue;
            var fields = line.Split('\t');
            if (fields.Length != names.Length)
                throw new InvalidDataException(
                    $"N-CEN data set table {name} line {lineNumber} has {fields.Length} fields; its header has {names.Length}."
                );
            rows.Add(fields);
        }
        return new NCenTsvTable(name, columns, rows);
    }

    /// <summary>Fails when a column every vintage carries is missing, so a format change is loud.</summary>
    internal void Require(params string[] columns)
    {
        var missing = columns.Where(column => !_columns.ContainsKey(column)).ToList();
        if (missing.Count > 0)
            throw new InvalidDataException(
                $"N-CEN data set table {_name} lacks column(s) {string.Join(", ", missing)}."
            );
    }

    /// <summary>The trimmed value, or null when blank or the column is absent.</summary>
    internal string Value(string[] row, string column)
    {
        if (!_columns.TryGetValue(column, out var index))
            return null;
        var value = row[index].Trim();
        return value.Length == 0 ? null : value;
    }
}
