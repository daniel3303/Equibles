namespace Equibles.Integrations.Lse.Xlsx;

// One spreadsheet row as text, indexed by column position; a cell the file omits reads as empty.
public sealed class XlsxRow(int number, IReadOnlyList<string> cells)
{
    public int Number { get; } = number;

    public IReadOnlyList<string> Cells { get; } = cells;

    public string Cell(int index) =>
        index >= 0 && index < Cells.Count ? Cells[index] ?? string.Empty : string.Empty;
}
