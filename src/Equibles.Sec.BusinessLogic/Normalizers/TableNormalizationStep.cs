using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;

namespace Equibles.Sec.BusinessLogic.Normalizers;

internal class TableNormalizationStep : IHtmlNormalizationStep
{
    private const string ColSpanAttribute = "colspan";
    private const string RowSpanAttribute = "rowspan";

    // Marks the cells a colspan expansion adds, so a header is moved only across its own span.
    private const string SpanFillAttribute = "data-colspan-fill";

    private static readonly Regex HtmlWhitespaceRun = new(@"[ \t\r\n\f]+", RegexOptions.Compiled);

    // A figure as filings print it: grouped digits or a decimal, signed by parentheses, a dash or a
    // currency sign, or a lone dash for nil. A bare four-digit year is a header, never a figure.
    private static readonly Regex Figure = new(
        @"^\(?[-−–]?\$?\s?(?:\d{1,3}(?:,\d{3})*(?:\.\d+)?|\d+\.\d+)\)?%?$|^[-−–—]$",
        RegexOptions.Compiled
    );

    public void Execute(IHtmlDocument doc)
    {
        var tables = doc.QuerySelectorAll("table").ToList();
        if (tables.Count == 0)
            return;

        foreach (var table in tables)
        {
            CollapseCellWhitespace(table);
            FixColspan(table, doc);
            FixRowspan(table, doc);
            RemoveEmptyRows(table);
            RemoveEmptyColumns(table);
            MergeSuffixColumns(table);
            MoveHeadersOverFigures(table);
            ClearBlankCells(table);
        }

        foreach (var cell in doc.QuerySelectorAll($"[{SpanFillAttribute}]"))
        {
            cell.RemoveAttribute(SpanFillAttribute);
        }
    }

    // HTML renders a line break inside text as a space, but the Markdown converter writes one inside
    // a cell as <br>; filers wrap their source lines mid-label ("Cash\nFlows"), so collapse them first.
    private static void CollapseCellWhitespace(IElement table)
    {
        var texts = table
            .Descendants<IText>()
            .Where(text => text.ParentElement?.Closest("pre") == null);
        foreach (var text in texts)
        {
            text.Data = HtmlWhitespaceRun.Replace(text.Data, " ");
        }
    }

    private void FixColspan(IElement table, IHtmlDocument doc)
    {
        var cellsWithColspan = table.QuerySelectorAll("td[colspan], th[colspan]").ToList();
        if (cellsWithColspan.Count == 0)
            return;

        foreach (var cell in cellsWithColspan)
        {
            var colspanAttr = cell.GetAttribute(ColSpanAttribute) ?? "1";
            if (!int.TryParse(colspanAttr, out var colspanValue) || colspanValue <= 1)
            {
                continue;
            }

            cell.RemoveAttribute(ColSpanAttribute);

            // A cell that also spans rows occupies a rectangular block; carry its
            // rowspan onto the new colspan cells so FixRowspan fills the full width
            // of every spanned row (otherwise those rows come out short by
            // colspan - 1 cells and the grid is ragged).
            var rowspanAttr = cell.GetAttribute(RowSpanAttribute);

            var currentCell = cell;
            for (int i = 1; i < colspanValue; i++)
            {
                var newCell = doc.CreateElement("td");
                newCell.SetAttribute(SpanFillAttribute, "");
                if (rowspanAttr != null)
                {
                    newCell.SetAttribute(RowSpanAttribute, rowspanAttr);
                }
                if (currentCell.ParentElement != null)
                {
                    HtmlElementExtensions.InsertAfter(
                        currentCell.ParentElement,
                        newCell,
                        currentCell
                    );
                    currentCell = newCell;
                }
            }
        }
    }

    private void FixRowspan(IElement table, IHtmlDocument doc)
    {
        var cellsWithRowspan = table.QuerySelectorAll("td[rowspan], th[rowspan]").ToList();
        if (cellsWithRowspan.Count == 0)
            return;

        foreach (var cell in cellsWithRowspan)
        {
            var rowspanAttr = cell.GetAttribute(RowSpanAttribute) ?? "1";
            if (!int.TryParse(rowspanAttr, out var rowspanValue) || rowspanValue <= 1)
            {
                continue;
            }

            var currentRow = cell.ParentElement;
            if (currentRow == null)
                continue;

            var cells = HtmlElementExtensions.DirectChildCells(currentRow);
            var cellIndex = cells.IndexOf(cell);

            if (cellIndex == -1)
                continue;

            cell.RemoveAttribute(RowSpanAttribute);

            var nextRow = currentRow.NextElementSibling;
            for (int i = 1; i < rowspanValue; i++)
            {
                while (nextRow != null && nextRow.LocalName != "tr")
                {
                    nextRow = nextRow.NextElementSibling;
                }

                if (nextRow != null)
                {
                    InsertEmptyCellAtIndex(nextRow, cellIndex, doc);
                    nextRow = nextRow.NextElementSibling;
                }
            }
        }
    }

    private void InsertEmptyCellAtIndex(IElement row, int cellIndex, IHtmlDocument doc)
    {
        var newCell = doc.CreateElement("td");
        var existingCells = HtmlElementExtensions.DirectChildCells(row);

        if (cellIndex < existingCells.Count)
        {
            row.InsertBefore(newCell, existingCells[cellIndex]);
        }
        else
        {
            row.AppendChild(newCell);
        }
    }

    private void RemoveEmptyRows(IElement table)
    {
        var rows = table.QuerySelectorAll("tr").ToList();

        foreach (var row in rows)
        {
            if (IsRowEmpty(row))
            {
                row.Remove();
            }
        }
    }

    private static bool IsRowEmpty(IElement row) =>
        HtmlElementExtensions.DirectChildCells(row).All(IsCellEmpty);

    // Empty whatever wraps the blank (<p>&nbsp;</p>, <font>, a zero-width space), unless the cell
    // holds something visual without text: a logo, signature or checkmark image, a link, a form field.
    private static bool IsCellEmpty(IElement cell) =>
        HtmlElementExtensions.IsBlankText(cell.TextContent)
        && cell.QuerySelector(MeaningfulNonTextSelector) == null;

    private const string MeaningfulNonTextSelector =
        "img, a[href], input, svg, object, embed, iframe, video, audio, canvas, picture";

    private void RemoveEmptyColumns(IElement table)
    {
        var rows = table.QuerySelectorAll("tr").ToList();
        if (rows.Count == 0)
            return;

        var maxCols = rows.Max(row => HtmlElementExtensions.DirectChildCells(row).Count);
        var columnsToRemove = new List<int>();

        for (int colIndex = 0; colIndex < maxCols; colIndex++)
        {
            if (IsColumnEmpty(rows, colIndex))
            {
                columnsToRemove.Add(colIndex);
            }
        }

        RemoveColumnsByIndex(rows, columnsToRemove);
    }

    private bool IsColumnEmpty(List<IElement> rows, int colIndex) =>
        rows.All(row =>
        {
            var cells = HtmlElementExtensions.DirectChildCells(row);
            return colIndex >= cells.Count || IsCellEmpty(cells[colIndex]);
        });

    private void RemoveColumnsByIndex(List<IElement> rows, List<int> columnsToRemove)
    {
        foreach (var colIndex in columnsToRemove.OrderByDescending(x => x))
        {
            foreach (var row in rows)
            {
                var cells = HtmlElementExtensions.DirectChildCells(row);
                if (colIndex < cells.Count)
                {
                    cells[colIndex].Remove();
                }
            }
        }
    }

    private static readonly HashSet<string> ClosingSuffixes = [")", "%", ")%", "%)"];

    // Filers put a negative's closing parenthesis or a percent sign in a column of its own
    // ("(8,036" | ")"); fold such a column into the figures on its left so each reads whole.
    private static void MergeSuffixColumns(IElement table)
    {
        var rows = table.QuerySelectorAll("tr").ToList();
        if (rows.Count == 0)
            return;

        var maxCols = rows.Max(row => HtmlElementExtensions.DirectChildCells(row).Count);
        for (var colIndex = maxCols - 1; colIndex >= 1; colIndex--)
        {
            if (!IsSuffixColumn(rows, colIndex))
                continue;

            foreach (var row in rows)
            {
                var cells = HtmlElementExtensions.DirectChildCells(row);
                if (colIndex >= cells.Count)
                    continue;

                var suffix = HtmlElementExtensions.TrimBlank(cells[colIndex].TextContent);
                if (suffix.Length > 0)
                    AppendToFigure(cells[colIndex - 1], suffix);
                cells[colIndex].Remove();
            }
        }
    }

    // The suffix joins the figure's own text node, so the converter cannot space them apart.
    private static void AppendToFigure(IElement cell, string suffix)
    {
        var figure = cell.Descendants<IText>()
            .Last(text => !HtmlElementExtensions.IsBlankText(text.Data));
        figure.Data = figure.Data.TrimEnd() + suffix;
    }

    // Every filled cell of the column is a suffix and follows a figure, so nothing else is merged.
    private static bool IsSuffixColumn(List<IElement> rows, int colIndex)
    {
        var hasSuffix = false;
        foreach (var row in rows)
        {
            var cells = HtmlElementExtensions.DirectChildCells(row);
            if (colIndex >= cells.Count)
                continue;

            var text = HtmlElementExtensions.TrimBlank(cells[colIndex].TextContent);
            if (text.Length == 0)
                continue;

            if (!ClosingSuffixes.Contains(text) || !EndsWithFigure(cells[colIndex - 1], text))
                return false;
            hasSuffix = true;
        }

        return hasSuffix;
    }

    // A figure ends in a digit; a percent may also follow a negative already closed, as in "(7)".
    private static bool EndsWithFigure(IElement cell, string suffix)
    {
        var figure = HtmlElementExtensions.TrimBlank(cell.TextContent);
        if (figure.Length == 0)
            return false;

        return char.IsDigit(figure[^1]) || (figure[^1] == ')' && suffix == "%");
    }

    // The padding of a kept blank cell (a zero-width space, &nbsp;) carries into the Markdown as noise.
    private static void ClearBlankCells(IElement table)
    {
        foreach (var cell in table.QuerySelectorAll("td, th").Where(IsCellEmpty))
        {
            cell.InnerHtml = string.Empty;
        }
    }

    // A header spanning a spacer and a figure column lands, once its colspan is expanded, over the
    // spacer while the figures sit one column right ("% Change" | "" over "" | "12.2"). Move a column
    // that holds only header text into its right neighbour when each header spanned that neighbour
    // and the neighbour holds figures; a header over a column of its own stays, as does the label column.
    private static void MoveHeadersOverFigures(IElement table)
    {
        var rows = table.QuerySelectorAll("tr").ToList();
        if (rows.Count == 0)
            return;

        var maxCols = rows.Max(row => HtmlElementExtensions.DirectChildCells(row).Count);
        for (var colIndex = maxCols - 2; colIndex >= 1; colIndex--)
        {
            if (!IsHeaderOverFigures(rows, colIndex))
                continue;

            foreach (var row in rows)
            {
                var cells = HtmlElementExtensions.DirectChildCells(row);
                if (colIndex >= cells.Count)
                    continue;

                if (!IsCellEmpty(cells[colIndex]))
                    cells[colIndex + 1].InnerHtml = cells[colIndex].InnerHtml;
                cells[colIndex].Remove();
            }
        }
    }

    private static bool IsHeaderOverFigures(List<IElement> rows, int colIndex)
    {
        var hasHeader = false;
        var hasFigure = false;
        foreach (var row in rows)
        {
            var cells = HtmlElementExtensions.DirectChildCells(row);
            var header = colIndex < cells.Count && !IsCellEmpty(cells[colIndex]);
            var next = colIndex + 1 < cells.Count && !IsCellEmpty(cells[colIndex + 1]);
            if (header)
            {
                if (
                    next
                    || colIndex + 1 >= cells.Count
                    || !cells[colIndex + 1].HasAttribute(SpanFillAttribute)
                    || IsFigure(cells[colIndex])
                )
                    return false;
                hasHeader = true;
            }
            else if (next && IsFigure(cells[colIndex + 1]))
            {
                hasFigure = true;
            }
        }

        return hasHeader && hasFigure;
    }

    private static bool IsFigure(IElement cell) =>
        Figure.IsMatch(HtmlElementExtensions.TrimBlank(cell.TextContent));
}
