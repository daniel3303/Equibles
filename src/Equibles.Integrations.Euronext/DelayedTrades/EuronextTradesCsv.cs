using System.Text;

namespace Equibles.Integrations.Euronext.DelayedTrades;

// The trades file quotes every field; a quote inside a field is doubled, and a bare field is accepted too.
public static class EuronextTradesCsv
{
    public static string[] Split(string line, int expectedColumns)
    {
        if (line == null)
            return null;
        var fields = new List<string>(expectedColumns);
        var field = new StringBuilder();
        var quoted = false;
        var index = 0;
        while (index < line.Length)
        {
            var character = line[index];
            if (quoted)
            {
                if (character == '"')
                {
                    if (index + 1 < line.Length && line[index + 1] == '"')
                    {
                        field.Append('"');
                        index += 2;
                        continue;
                    }
                    quoted = false;
                    index++;
                    continue;
                }
                field.Append(character);
                index++;
                continue;
            }
            switch (character)
            {
                case '"':
                    quoted = true;
                    break;
                case ',':
                    fields.Add(field.ToString());
                    field.Clear();
                    break;
                default:
                    field.Append(character);
                    break;
            }
            index++;
        }
        if (quoted)
            return null;
        fields.Add(field.ToString());
        return fields.Count == expectedColumns ? fields.ToArray() : null;
    }
}
