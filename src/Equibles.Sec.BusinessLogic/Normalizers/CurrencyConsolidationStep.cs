using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;

namespace Equibles.Sec.BusinessLogic.Normalizers;

internal class CurrencyConsolidationStep : IHtmlNormalizationStep
{
    private static readonly Dictionary<string, (string Symbol, string Name)> CurrencyMap = new()
    {
        ["USD"] = ("$", "US Dollars"),
        ["EUR"] = ("€", "Euros"),
        ["GBP"] = ("£", "British Pounds"),
        ["JPY"] = ("¥", "Japanese Yen"),
        ["INR"] = ("₹", "Indian Rupees"),
    };

    // Word-boundary match per code so acronyms like USDA / USDC / EUREKA that
    // embed an ISO code as a substring are not mistaken for currency cells.
    private static readonly Dictionary<string, Regex> CurrencyCodeRegexes =
        CurrencyMap.Keys.ToDictionary(
            code => code,
            code => new Regex($@"\b{Regex.Escape(code)}\b", RegexOptions.Compiled)
        );

    private static readonly Regex CurrencyScaleRegex = new(
        @"^(?:(?:amounts?\s+)?in\s+)?(?:thousands|millions|billions|trillions)(?:\s*,?\s*except\s+(?:per[- ]share(?:\s+(?:amounts?|data))?|share(?:s)?))?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );

    private static readonly Regex UsDollarRegex = new(
        @"\bUS\$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );

    public void Execute(IHtmlDocument doc)
    {
        var tables = doc.QuerySelectorAll("table").ToList();
        if (tables.Count == 0)
            return;

        foreach (var table in tables)
        {
            var detectedCurrency = ConsolidateCurrencyColumns(table);
            if (detectedCurrency != null)
            {
                AddCurrencyNote(table, detectedCurrency, doc);
            }
        }
    }

    private string ConsolidateCurrencyColumns(IElement table)
    {
        var rows = table.QuerySelectorAll("tr").ToList();
        if (rows.Count == 0)
            return null;

        var maxCols = rows.Max(row => HtmlElementExtensions.DirectChildCells(row).Count);
        var columnsToProcess = new List<int>();
        var currencySymbols = new HashSet<string>();

        for (int colIndex = 0; colIndex < maxCols - 1; colIndex++)
        {
            var columnCurrencySymbols = new HashSet<string>();
            if (ShouldConsolidateColumn(rows, colIndex, columnCurrencySymbols))
            {
                columnsToProcess.Add(colIndex);
                currencySymbols.UnionWith(columnCurrencySymbols);
            }
        }

        ProcessCurrencyColumnsForConsolidation(rows, columnsToProcess);

        return currencySymbols.Count == 1 ? currencySymbols.Single() : null;
    }

    private bool ShouldConsolidateColumn(
        List<IElement> rows,
        int colIndex,
        HashSet<string> currencySymbols
    )
    {
        var shouldConsolidate = false;

        foreach (var row in rows)
        {
            var cells = HtmlElementExtensions.DirectChildCells(row);
            if (colIndex >= cells.Count || (colIndex + 1) >= cells.Count)
                continue;

            var currentCell = cells[colIndex].TextContent.Trim();
            if (IsEmptyCell(currentCell))
                continue;

            var detectedCurrency = DetectCurrency(currentCell);
            var nextCell = cells[colIndex + 1].TextContent.Trim();
            if (
                detectedCurrency == null
                || (!IsStandaloneCurrencyCell(currentCell) && !IsEmptyCell(nextCell))
            )
                return false;

            currencySymbols.Add(detectedCurrency);
            shouldConsolidate = true;
        }

        return shouldConsolidate;
    }

    private bool IsStandaloneCurrencyCell(string text)
    {
        var remainder = RemoveCurrencySymbols(text).Trim(' ', '\t', '\r', '\n', '(', ')');
        return remainder.Length == 0 || CurrencyScaleRegex.IsMatch(remainder);
    }

    private bool IsEmptyCell(string cellText)
    {
        return string.IsNullOrWhiteSpace(cellText) || cellText == "\u00A0";
    }

    private string DetectCurrency(string text)
    {
        foreach (var (code, (symbol, _)) in CurrencyMap)
        {
            if (ContainsCurrencySymbol(text, symbol) || CurrencyCodeRegexes[code].IsMatch(text))
                return code;
        }
        return null;
    }

    // A symbol immediately preceded by a letter is a different currency's
    // prefixed symbol (e.g. "C$", "A$", "HK$", "NZ$"), not this one — so a
    // Canadian/Australian/HK dollar cell is not mistaken for US Dollars,
    // mirroring the boundary-careful currency-code match above. The one
    // exception is "US$": the "US" prefix is the standard, unambiguous
    // notation for US Dollars, so it must NOT be dropped as foreign.
    private static bool ContainsCurrencySymbol(string text, string symbol)
    {
        var index = text.IndexOf(symbol, StringComparison.Ordinal);
        while (index >= 0)
        {
            if (
                index == 0
                || !char.IsLetter(text[index - 1])
                || IsUsDollarPrefix(text, index, symbol)
            )
                return true;
            index = text.IndexOf(symbol, index + symbol.Length, StringComparison.Ordinal);
        }
        return false;
    }

    // "US$" denotes US Dollars, not a foreign prefixed symbol: the "$" is
    // preceded by exactly the letters "US". Returns true only for that case.
    private static bool IsUsDollarPrefix(string text, int symbolIndex, string symbol)
    {
        if (symbol != "$")
            return false;

        var start = symbolIndex;
        while (start > 0 && char.IsLetter(text[start - 1]))
            start--;

        return text.AsSpan(start, symbolIndex - start)
            .Equals("US", StringComparison.OrdinalIgnoreCase);
    }

    private void ProcessCurrencyColumnsForConsolidation(
        List<IElement> rows,
        List<int> columnsToProcess
    )
    {
        foreach (var colIndex in columnsToProcess.OrderByDescending(x => x))
        {
            foreach (var row in rows)
            {
                var cells = HtmlElementExtensions.DirectChildCells(row);
                if (colIndex >= cells.Count)
                    continue;

                var currentCell = cells[colIndex];
                var currentText = currentCell.TextContent.Trim();

                if (!IsEmptyCell(currentText) && DetectCurrency(currentText) == null)
                    continue;

                var cleanTitle = RemoveCurrencySymbols(currentText);
                if (cleanTitle.Length > 0 && (colIndex + 1) >= cells.Count)
                    continue;

                if (cleanTitle.Length > 0)
                {
                    var nextCell = cells[colIndex + 1];
                    nextCell.InnerHtml = IsEmptyCell(nextCell.TextContent.Trim())
                        ? cleanTitle
                        : $"{cleanTitle} {nextCell.InnerHtml}";
                }

                currentCell.Remove();
            }
        }
    }

    private string RemoveCurrencySymbols(string text)
    {
        text = UsDollarRegex.Replace(text, "");
        foreach (var (code, (symbol, _)) in CurrencyMap)
        {
            text = text.Replace(symbol, "");
            text = CurrencyCodeRegexes[code].Replace(text, "");
        }
        return text.Trim();
    }

    private void AddCurrencyNote(IElement table, string currencyCode, IHtmlDocument doc)
    {
        if (table.ParentElement == null)
            return;

        var currencyName = GetCurrencyName(currencyCode);
        var p = doc.CreateElement("p");
        var em = doc.CreateElement("em");
        em.TextContent = $"All values are in {currencyName}.";
        p.AppendChild(em);
        HtmlElementExtensions.InsertAfter(table.ParentElement, p, table);
    }

    private string GetCurrencyName(string currencyCode)
    {
        return CurrencyMap.TryGetValue(currencyCode, out var currency)
            ? currency.Name
            : currencyCode;
    }
}
