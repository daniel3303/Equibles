using System.IO.Compression;
using System.Text;
using System.Xml;

namespace Equibles.Integrations.Lse.Xlsx;

// The little of the Office Open XML spreadsheet format a published instrument list needs: the sheet a name
// points at, read as text. Only the parts asked for are decompressed, so the unread sheets cost nothing.
public sealed class XlsxWorkbook : IDisposable
{
    private const string WorkbookPath = "xl/workbook.xml";
    private const string RelationshipsPath = "xl/_rels/workbook.xml.rels";
    private const string SharedStringsPath = "xl/sharedStrings.xml";

    // The format's own last column, XFD, and a ceiling on the cells one sheet may lay out. A sheet states its
    // own widths, so without the second bound a few kilobytes of references can ask for gigabytes of rows.
    private const int MaxColumns = 16_384;
    private const int MaxCells = 4_000_000;
    private const string RelationshipNamespace =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    private readonly ZipArchive _archive;
    private readonly long _maxEntryBytes;
    private IReadOnlyList<string> _sharedStrings;

    private XlsxWorkbook(ZipArchive archive, long maxEntryBytes)
    {
        _archive = archive;
        _maxEntryBytes = maxEntryBytes;
    }

    public static XlsxWorkbook Open(byte[] bytes, long maxEntryBytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        try
        {
            return new XlsxWorkbook(
                new ZipArchive(new MemoryStream(bytes, false), ZipArchiveMode.Read),
                maxEntryBytes
            );
        }
        catch (InvalidDataException exception)
        {
            throw new InvalidDataException("Workbook is not a readable archive.", exception);
        }
    }

    public IReadOnlyList<XlsxRow> ReadSheet(string sheetName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sheetName);
        var relationshipId = FindSheetRelationship(sheetName);
        var path = ResolveSheetPath(relationshipId);
        var strings = SharedStrings();
        var rows = new List<XlsxRow>();
        var cells = 0;
        using var reader = OpenXml(path);
        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "row")
                continue;
            if (!int.TryParse(reader.GetAttribute("r"), out var number))
                number = rows.Count + 1;
            if (reader.IsEmptyElement)
            {
                rows.Add(new XlsxRow(number, []));
                continue;
            }
            var row = ReadCells(reader.ReadSubtree(), strings);
            cells += row.Count;
            if (cells > MaxCells)
                throw new InvalidDataException(
                    $"Workbook sheet \"{sheetName}\" is larger than it may be."
                );
            rows.Add(new XlsxRow(number, row));
        }
        return rows;
    }

    private static List<string> ReadCells(XmlReader row, IReadOnlyList<string> strings)
    {
        var cells = new List<string>();
        using (row)
            while (row.Read())
            {
                if (row.NodeType != XmlNodeType.Element || row.LocalName != "c")
                    continue;
                var index = ColumnIndex(row.GetAttribute("r"), cells.Count);
                var type = row.GetAttribute("t");
                var value = row.IsEmptyElement ? Cell.Empty : ReadCell(row.ReadSubtree());
                while (cells.Count <= index)
                    cells.Add(string.Empty);
                cells[index] = Resolve(type, value, strings);
            }
        return cells;
    }

    private static Cell ReadCell(XmlReader cell)
    {
        var value = new StringBuilder();
        var inline = new StringBuilder();
        var depth = 0;
        var text = 0;
        using (cell)
            while (cell.Read())
                switch (cell.NodeType)
                {
                    case XmlNodeType.Element when cell.LocalName == "v" && !cell.IsEmptyElement:
                        depth++;
                        break;
                    case XmlNodeType.Element when cell.LocalName == "t" && !cell.IsEmptyElement:
                        text++;
                        break;
                    case XmlNodeType.EndElement when cell.LocalName == "v":
                        depth--;
                        break;
                    case XmlNodeType.EndElement when cell.LocalName == "t":
                        text--;
                        break;
                    case XmlNodeType.Text
                    or XmlNodeType.CDATA
                    or XmlNodeType.SignificantWhitespace
                    or XmlNodeType.Whitespace:
                        if (depth > 0)
                            value.Append(cell.Value);
                        else if (text > 0)
                            inline.Append(cell.Value);
                        break;
                }
        return new Cell(value.ToString(), inline.ToString());
    }

    private static string Resolve(string type, Cell cell, IReadOnlyList<string> strings)
    {
        if (type == "inlineStr")
            return cell.Inline;
        if (type != "s")
            return cell.Value;
        if (!int.TryParse(cell.Value, out var index) || index < 0 || index >= strings.Count)
            throw new InvalidDataException("Workbook cell names a shared string it does not hold.");
        return strings[index];
    }

    // A1-style reference: the letters are the column. A cell without one continues from the previous cell, and
    // one naming a column past the format's last is refused rather than sized into an array of that width.
    private static int ColumnIndex(string reference, int fallback)
    {
        if (string.IsNullOrEmpty(reference))
            return fallback;
        var index = 0;
        var letters = 0;
        foreach (var character in reference)
        {
            if (character is < 'A' or > 'Z')
                break;
            index = index * 26 + (character - 'A' + 1);
            letters++;
            if (index > MaxColumns)
                throw new InvalidDataException("Workbook cell names a column outside the sheet.");
        }
        return letters == 0 ? fallback : index - 1;
    }

    private IReadOnlyList<string> SharedStrings()
    {
        if (_sharedStrings != null)
            return _sharedStrings;
        if (_archive.Entries.All(entry => !Matches(entry.FullName, SharedStringsPath)))
            return _sharedStrings = [];
        var strings = new List<string>();
        using var reader = OpenXml(SharedStringsPath);
        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "si")
                continue;
            strings.Add(
                reader.IsEmptyElement ? string.Empty : ReadCell(reader.ReadSubtree()).Inline
            );
        }
        return _sharedStrings = strings;
    }

    private string FindSheetRelationship(string sheetName)
    {
        using var reader = OpenXml(WorkbookPath);
        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "sheet")
                continue;
            if (reader.GetAttribute("name") != sheetName)
                continue;
            var relationshipId = reader.GetAttribute("id", RelationshipNamespace);
            if (!string.IsNullOrEmpty(relationshipId))
                return relationshipId;
        }
        throw new InvalidDataException($"Workbook has no sheet named \"{sheetName}\".");
    }

    private string ResolveSheetPath(string relationshipId)
    {
        using var reader = OpenXml(RelationshipsPath);
        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "Relationship")
                continue;
            if (reader.GetAttribute("Id") != relationshipId)
                continue;
            var target = reader.GetAttribute("Target");
            if (
                string.IsNullOrWhiteSpace(target) || target.Contains("..", StringComparison.Ordinal)
            )
                break;
            return target.StartsWith('/') ? target.TrimStart('/') : "xl/" + target;
        }
        throw new InvalidDataException("Workbook does not say where its sheet is stored.");
    }

    private XmlReader OpenXml(string path)
    {
        var entry =
            _archive.Entries.FirstOrDefault(candidate => Matches(candidate.FullName, path))
            ?? throw new InvalidDataException($"Workbook is missing {path}.");
        if (entry.Length > _maxEntryBytes)
            throw new InvalidDataException($"Workbook part {path} exceeds the read limit.");
        return XmlReader.Create(
            new BoundedStream(entry.Open(), _maxEntryBytes),
            new XmlReaderSettings
            {
                CloseInput = true,
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                IgnoreComments = true,
                IgnoreProcessingInstructions = true,
            }
        );
    }

    private static bool Matches(string entry, string path) =>
        string.Equals(entry, path, StringComparison.OrdinalIgnoreCase);

    public void Dispose() => _archive.Dispose();

    private readonly record struct Cell(string Value, string Inline)
    {
        public static Cell Empty { get; } = new(string.Empty, string.Empty);
    }
}
