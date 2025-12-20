using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;

namespace Equibles.Sec.BusinessLogic.Normalizers;

internal class TableNormalizationStep : IHtmlNormalizationStep {
    private readonly HtmlParser _parser;

    internal TableNormalizationStep(HtmlParser parser) {
        _parser = parser;
    }

    public void Execute(IHtmlDocument doc) {
        var tables = doc.QuerySelectorAll("table").ToList();
        if (tables.Count == 0) return;

        foreach (var table in tables) {
            FixColspanAndRowspan(table, doc);
            CleanupTableRows(table);
            RemoveEmptyColumns(table);
        }
    }

    private void FixColspanAndRowspan(IElement table, IHtmlDocument doc) {
        FixColspan(table, doc);
        FixRowspan(table, doc);
    }

    private void FixColspan(IElement table, IHtmlDocument doc) {
        var cellsWithColspan = table.QuerySelectorAll("td[colspan], th[colspan]").ToList();
        if (cellsWithColspan.Count == 0) return;

        foreach (var cell in cellsWithColspan) {
            var colspanAttr = cell.GetAttribute("colspan") ?? "1";
            if (!int.TryParse(colspanAttr, out var colspanValue) || colspanValue <= 1) {
                continue;
            }

            cell.RemoveAttribute("colspan");

            var currentCell = cell;
            for (int i = 1; i < colspanValue; i++) {
                var newCell = doc.CreateElement("td");
                if (currentCell.ParentElement != null) {
                    HtmlElementExtensions.InsertAfter(currentCell.ParentElement, newCell, currentCell);
                    currentCell = newCell;
                }
            }
        }
    }

    private void FixRowspan(IElement table, IHtmlDocument doc) {
        var cellsWithRowspan = table.QuerySelectorAll("td[rowspan], th[rowspan]").ToList();
        if (cellsWithRowspan.Count == 0) return;

        foreach (var cell in cellsWithRowspan) {
            var rowspanAttr = cell.GetAttribute("rowspan") ?? "1";
            if (!int.TryParse(rowspanAttr, out var rowspanValue) || rowspanValue <= 1) {
                continue;
            }

            var currentRow = cell.ParentElement;
            if (currentRow == null) continue;

            var cells = HtmlElementExtensions.DirectChildCells(currentRow);
            var cellIndex = cells.IndexOf(cell);

            if (cellIndex == -1) continue;

            cell.RemoveAttribute("rowspan");

            var nextRow = currentRow.NextElementSibling;
            for (int i = 1; i < rowspanValue; i++) {
                while (nextRow != null && nextRow.LocalName != "tr") {
                    nextRow = nextRow.NextElementSibling;
                }

                if (nextRow != null) {
                    InsertEmptyCellAtIndex(nextRow, cellIndex, doc);
                    nextRow = nextRow.NextElementSibling;
                }
            }
        }
    }

    private void InsertEmptyCellAtIndex(IElement row, int cellIndex, IHtmlDocument doc) {
        var newCell = doc.CreateElement("td");
        var existingCells = HtmlElementExtensions.DirectChildCells(row);

        if (cellIndex < existingCells.Count) {
            row.InsertBefore(newCell, existingCells[cellIndex]);
        } else {
            row.AppendChild(newCell);
        }
    }

    private void CleanupTableRows(IElement table) {
        RemoveEmptyRows(table);
    }

    private void RemoveEmptyRows(IElement table) {
        var rows = table.QuerySelectorAll("tr").ToList();

        foreach (var row in rows) {
            if (IsRowEmpty(row)) {
                row.Remove();
            }
        }
    }

    private bool IsRowEmpty(IElement row) {
        var cells = HtmlElementExtensions.DirectChildCells(row);

        foreach (var cell in cells) {
            var cellText = cell.TextContent.Trim();
            var cellHtml = cell.InnerHtml.Trim();

            if (!string.IsNullOrWhiteSpace(cellText) &&
                cellText != "\u00A0" &&
                cellText != " ") {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(cellHtml) &&
                cellHtml != "&nbsp;" &&
                cellHtml != "&#160;" &&
                !cellHtml.Contains(
                    "white-space:pre-wrap;font-family:Arial;font-kerning:none;min-width:fit-content;\"> </span>") &&
                !IsOnlyWhitespaceSpan(cellHtml)) {
                return false;
            }
        }

        return true;
    }

    private bool IsOnlyWhitespaceSpan(string html) {
        var tempDoc = _parser.ParseDocument(html);

        var spans = tempDoc.QuerySelectorAll("span").ToList();
        if (spans.Count == 0) return true;

        foreach (var span in spans) {
            var spanText = span.TextContent.Trim();
            if (!string.IsNullOrWhiteSpace(spanText) && spanText != "\u00A0") {
                return false;
            }
        }

        return true;
    }

    private void RemoveEmptyColumns(IElement table) {
        var rows = table.QuerySelectorAll("tr").ToList();
        if (rows.Count == 0) return;

        var maxCols = rows.Max(row => HtmlElementExtensions.DirectChildCells(row).Count);
        var columnsToRemove = new List<int>();

        for (int colIndex = 0; colIndex < maxCols; colIndex++) {
            if (IsColumnEmpty(rows, colIndex)) {
                columnsToRemove.Add(colIndex);
            }
        }

        RemoveColumnsByIndex(rows, columnsToRemove);
    }

    private bool IsColumnEmpty(List<IElement> rows, int colIndex) {
        foreach (var row in rows) {
            var cells = HtmlElementExtensions.DirectChildCells(row);
            if (colIndex < cells.Count) {
                var cellText = cells[colIndex].TextContent.Trim();
                if (!string.IsNullOrWhiteSpace(cellText) && cellText != "\u00A0") {
                    return false;
                }
            }
        }

        return true;
    }

    private void RemoveColumnsByIndex(List<IElement> rows, List<int> columnsToRemove) {
        foreach (var colIndex in columnsToRemove.OrderByDescending(x => x)) {
            foreach (var row in rows) {
                var cells = HtmlElementExtensions.DirectChildCells(row);
                if (colIndex < cells.Count) {
                    cells[colIndex].Remove();
                }
            }
        }
    }
}
