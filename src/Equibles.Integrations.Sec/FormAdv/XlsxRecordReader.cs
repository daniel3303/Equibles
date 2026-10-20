using System.Globalization;
using System.Text;
using ExcelDataReader;

namespace Equibles.Integrations.Sec.FormAdv;

/// <summary>
/// Reads the first worksheet of an .xlsx workbook as text records, the shape
/// <see cref="CsvRecordReader"/> yields, so one parser serves both. Numbers come out
/// culture-invariant without a decimal point when whole, date cells as MM/dd/yyyy, and empty
/// cells as empty strings.
/// </summary>
public static class XlsxRecordReader
{
    // ExcelDataReader resolves a Windows code page while it configures itself, which .NET
    // provides only once the code-page provider is registered.
    static XlsxRecordReader() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    /// <summary>Reads rows from a seekable <paramref name="workbook"/> stream.</summary>
    public static IEnumerable<List<string>> Read(Stream workbook)
    {
        using var reader = ExcelReaderFactory.CreateOpenXmlReader(workbook);
        while (reader.Read())
        {
            var record = new List<string>(reader.FieldCount);
            for (var column = 0; column < reader.FieldCount; column++)
                record.Add(Text(reader.GetValue(column)));
            yield return record;
        }
    }

    internal static string Text(object value) =>
        value switch
        {
            null => "",
            string text => text,
            double number when number == Math.Floor(number) && Math.Abs(number) < 1e15 => (
                (long)number
            ).ToString(CultureInfo.InvariantCulture),
            double number => number.ToString("R", CultureInfo.InvariantCulture),
            DateTime date => date.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture),
            bool flag => flag ? "Y" : "N",
            _ => Convert.ToString(value, CultureInfo.InvariantCulture),
        };
}
