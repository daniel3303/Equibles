using System.Text;

namespace Equibles.Integrations.Sec.FormAdv;

/// <summary>
/// A minimal streaming CSV reader covering the parts of RFC 4180 the SEC's Form ADV export
/// actually uses: comma delimiters, double-quote quoting, doubled quotes ("") as a literal
/// quote, and commas or line breaks embedded inside quoted fields (adviser names routinely
/// contain commas, e.g. "SMITH, BROWN &amp; GROOVER, INC."). Records are produced one at a
/// time so a 15k-row, 14&#160;MB file never has to be materialised in full.
/// </summary>
public static class CsvRecordReader
{
    /// <summary>
    /// Reads logical CSV records from <paramref name="reader"/>, each as its list of field
    /// values. A field spanning multiple physical lines (because it is quoted) is returned as
    /// a single value with the line breaks preserved.
    /// </summary>
    public static IEnumerable<List<string>> Read(TextReader reader)
    {
        var fields = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var recordHasContent = false;

        int read;
        while ((read = reader.Read()) != -1)
        {
            var c = (char)read;

            if (inQuotes)
            {
                if (c == '"')
                {
                    // A doubled quote ("") inside a quoted field is a literal quote.
                    if (reader.Peek() == '"')
                    {
                        reader.Read();
                        field.Append('"');
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(c);
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    inQuotes = true;
                    recordHasContent = true;
                    break;
                case ',':
                    fields.Add(field.ToString());
                    field.Clear();
                    recordHasContent = true;
                    break;
                case '\r':
                    // Swallow the LF of a CRLF pair; a lone CR also ends the record.
                    if (reader.Peek() == '\n')
                    {
                        reader.Read();
                    }
                    fields.Add(field.ToString());
                    field.Clear();
                    yield return fields;
                    fields = new List<string>();
                    recordHasContent = false;
                    break;
                case '\n':
                    fields.Add(field.ToString());
                    field.Clear();
                    yield return fields;
                    fields = new List<string>();
                    recordHasContent = false;
                    break;
                default:
                    field.Append(c);
                    recordHasContent = true;
                    break;
            }
        }

        // Emit a trailing record when the file does not end with a newline.
        if (recordHasContent || field.Length > 0 || fields.Count > 0)
        {
            fields.Add(field.ToString());
            yield return fields;
        }
    }
}
