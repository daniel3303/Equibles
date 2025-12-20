using AngleSharp.Dom;
using AngleSharp.Html.Dom;

namespace Equibles.Sec.BusinessLogic.Normalizers;

internal class CurrencyConsolidationStep : IHtmlNormalizationStep {
    private static readonly Dictionary<string, (string Symbol, string Name)> CurrencyMap = new() {
        ["USD"] = ("$", "US Dollars"),
        ["EUR"] = ("€", "Euros"),
        ["GBP"] = ("£", "British Pounds"),
        ["JPY"] = ("¥", "Japanese Yen"),
        ["INR"] = ("₹", "Indian Rupees"),
    };

    public void Execute(IHtmlDocument doc) {
        var tables = doc.QuerySelectorAll("table").ToList();
        if (tables.Count == 0) return;

        foreach (var table in tables) {
            var detectedCurrency = ConsolidateCurrencyColumns(table);
            if (detectedCurrency != null) {
                AddCurrencyNote(table, detectedCurrency, doc);
            }
        }
    }

    private string ConsolidateCurrencyColumns(IElement table) {
        var rows = table.QuerySelectorAll("tr").ToList();
        if (rows.Count == 0) return null;

        var maxCols = rows.Max(row => HtmlElementExtensions.DirectChildCells(row).Count);
        var columnsToProcess = new List<int>();
        var currencySymbols = new HashSet<string>();

        for (int colIndex = 0; colIndex < maxCols - 1; colIndex++) {
            if (ShouldConsolidateColumn(rows, colIndex, currencySymbols)) {
                columnsToProcess.Add(colIndex);
            }
        }

        ProcessCurrencyColumnsForConsolidation(rows, columnsToProcess);

        return currencySymbols.FirstOrDefault();
    }

    private bool ShouldConsolidateColumn(List<IElement> rows, int colIndex, HashSet<string> currencySymbols) {
        var shouldConsolidate = false;

        foreach (var row in rows) {
            var cells = HtmlElementExtensions.DirectChildCells(row);
            if (colIndex >= cells.Count || (colIndex + 1) >= cells.Count) continue;

            var currentCell = cells[colIndex].TextContent.Trim();
            var nextCell = cells[colIndex + 1].TextContent.Trim();

            if (string.IsNullOrWhiteSpace(currentCell) && string.IsNullOrWhiteSpace(nextCell)) {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(currentCell) && IsEmptyCell(nextCell)) {
                var detectedCurrency = DetectCurrency(currentCell);
                if (detectedCurrency != null) {
                    currencySymbols.Add(detectedCurrency);
                    shouldConsolidate = true;
                }
            }
        }

        return shouldConsolidate && currencySymbols.Count > 0;
    }

    private bool IsEmptyCell(string cellText) {
        return string.IsNullOrWhiteSpace(cellText) || cellText == "\u00A0";
    }

    private string DetectCurrency(string text) {
        foreach (var (code, (symbol, _)) in CurrencyMap) {
            if (text.Contains(symbol) || text.Contains(code))
                return code;
        }
        return null;
    }

    private void ProcessCurrencyColumnsForConsolidation(List<IElement> rows, List<int> columnsToProcess) {
        foreach (var colIndex in columnsToProcess.OrderByDescending(x => x)) {
            foreach (var row in rows) {
                var cells = HtmlElementExtensions.DirectChildCells(row);
                if (colIndex >= cells.Count || (colIndex + 1) >= cells.Count) continue;

                var currentCell = cells[colIndex];
                var nextCell = cells[colIndex + 1];

                var cleanTitle = RemoveCurrencySymbols(currentCell.TextContent.Trim());
                nextCell.InnerHtml = cleanTitle;
                currentCell.Remove();
            }
        }
    }

    private string RemoveCurrencySymbols(string text) {
        foreach (var (code, (symbol, _)) in CurrencyMap) {
            text = text.Replace(symbol, "").Replace(code, "");
        }
        return text.Trim();
    }

    private void AddCurrencyNote(IElement table, string currencyCode, IHtmlDocument doc) {
        if (table.ParentElement == null) return;

        var currencyName = GetCurrencyName(currencyCode);
        var p = doc.CreateElement("p");
        var em = doc.CreateElement("em");
        em.TextContent = $"All values are in {currencyName}.";
        p.AppendChild(em);
        HtmlElementExtensions.InsertAfter(table.ParentElement, p, table);
    }

    private string GetCurrencyName(string currencyCode) {
        return CurrencyMap.TryGetValue(currencyCode, out var currency) ? currency.Name : currencyCode;
    }
}
