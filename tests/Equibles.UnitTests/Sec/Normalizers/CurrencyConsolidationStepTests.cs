using AngleSharp.Html.Parser;
using Equibles.Sec.BusinessLogic.Normalizers;

namespace Equibles.UnitTests.Sec.Normalizers;

public class CurrencyConsolidationStepTests
{
    private readonly CurrencyConsolidationStep _sut = new();
    private readonly HtmlParser _parser = new();

    [Fact]
    public void DollarColumnFollowedByEmptyColumn_RemovesCurrencyColumnAndAddsNote()
    {
        var html =
            @"<html><body><table>
  <tr><td>$</td><td></td><td>100</td></tr>
  <tr><td>$</td><td></td><td>200</td></tr>
</table></body></html>";

        var doc = _parser.ParseDocument(html);

        _sut.Execute(doc);

        var rows = doc.QuerySelectorAll("tr");
        foreach (var row in rows)
        {
            var cells = row.QuerySelectorAll("td");
            cells.Should().HaveCount(2);
            cells[0].TextContent.Trim().Should().BeEmpty();
        }

        var note = doc.QuerySelector("table + p em");
        note.Should().NotBeNull();
        note.TextContent.Should().Be("All values are in US Dollars.");
    }

    [Fact]
    public void TableWithoutCurrencySymbols_NoChanges()
    {
        var html =
            @"<html><body><table>
  <tr><td>Name</td><td>Value</td></tr>
  <tr><td>Apple</td><td>100</td></tr>
</table></body></html>";

        var doc = _parser.ParseDocument(html);
        var originalHtml = doc.DocumentElement.OuterHtml;

        _sut.Execute(doc);

        doc.DocumentElement.OuterHtml.Should().Be(originalHtml);
    }

    [Fact]
    public void EurColumnFollowedByEmptyColumn_AddsEuroNote()
    {
        var html =
            @"<html><body><table>
  <tr><td>€</td><td></td><td>500</td></tr>
  <tr><td>€</td><td></td><td>600</td></tr>
</table></body></html>";

        var doc = _parser.ParseDocument(html);

        _sut.Execute(doc);

        var note = doc.QuerySelector("table + p em");
        note.Should().NotBeNull();
        note.TextContent.Should().Be("All values are in Euros.");
    }

    [Fact]
    public void DocumentWithNoTables_DoesNotThrow()
    {
        var html = @"<html><body><p>No tables here.</p></body></html>";

        var doc = _parser.ParseDocument(html);

        var act = () => _sut.Execute(doc);

        act.Should().NotThrow();
    }

    [Fact]
    public void TextualCurrencyCode_IsDetectedAndConsolidated()
    {
        // DetectCurrency matches on EITHER the symbol ("$") OR the textual code
        // ("USD") for each entry in the currency map. The existing symbol-based
        // tests don't exercise the code branch (`text.Contains(code)`) — SEC
        // filings often label currency columns with the textual ISO code rather
        // than the glyph (e.g. "USD" header above an empty cell). Pin the code
        // path so a refactor that drops the `|| text.Contains(code)` half of
        // the OR can't silently break detection of textual-code columns.
        var html =
            @"<html><body><table>
  <tr><td>USD</td><td></td><td>100</td></tr>
  <tr><td>USD</td><td></td><td>200</td></tr>
</table></body></html>";

        var doc = _parser.ParseDocument(html);

        _sut.Execute(doc);

        var note = doc.QuerySelector("table + p em");
        note.Should().NotBeNull();
        note.TextContent.Should().Be("All values are in US Dollars.");
    }

    [Fact]
    public void CurrencyColumnPrecededByLabelColumn_StillConsolidatesCurrencyColumnOnly()
    {
        // Every existing pin places the currency symbol in the FIRST column (colIndex 0).
        // Real 10-K financial-statement tables have the form:
        //   | Label              | $   |        | 123,456 |
        //   | Cost of goods sold | $   |        | 234,567 |
        // i.e. a descriptive label in column 0, the $ glyph in column 1, an empty
        // separator in column 2, and the numeric value in column 3. The consolidation
        // loop walks every (col, col+1) pair via `for (colIndex = 0; colIndex < maxCols - 1; colIndex++)`.
        // For (col 0, col 1) it sees ("Label", "$") — IsEmptyCell("$") is false, so
        // nothing triggers. For (col 1, col 2) it sees ("$", "") — IsEmptyCell("") is
        // true AND DetectCurrency("$") matches USD, so column 1 is added to
        // columnsToProcess. The pin asserts: the label in column 0 survives, the $
        // symbol disappears from column 1, AND the column count drops by exactly one
        // (only the consolidated currency column is removed, not the label).
        //
        // The regression this catches: a refactor that pruned the outer loop to "only
        // examine column 0" (e.g. assuming currency is always the leading column —
        // which is what the existing tests would suggest) would compile cleanly, pass
        // every other [Fact] in this file, and then silently leave every real SEC
        // statement's $ column intact while emitting a misleading "All values are in
        // US Dollars" note below an unchanged table.
        var html =
            @"<html><body><table>
  <tr><td>Revenue</td><td>$</td><td></td><td>100</td></tr>
  <tr><td>Expenses</td><td>$</td><td></td><td>200</td></tr>
</table></body></html>";

        var doc = _parser.ParseDocument(html);

        _sut.Execute(doc);

        var rows = doc.QuerySelectorAll("tr");
        foreach (var row in rows)
        {
            var cells = row.QuerySelectorAll("td");
            cells.Should().HaveCount(3);
        }

        var firstRowCells = doc.QuerySelectorAll("tr").First().QuerySelectorAll("td");
        firstRowCells[0].TextContent.Trim().Should().Be("Revenue");

        var allCellTexts = doc.QuerySelectorAll("td").Select(c => c.TextContent).ToList();
        allCellTexts.Should().NotContain(t => t.Contains("$"));

        var note = doc.QuerySelector("table + p em");
        note.Should().NotBeNull();
        note.TextContent.Should().Be("All values are in US Dollars.");
    }

    [Fact]
    public void GbpColumnFollowedByEmptyColumn_AddsBritishPoundsNote()
    {
        // CurrencyConsolidationStep's CurrencyMap has five entries: USD, EUR,
        // GBP, JPY, INR. The existing pins exercise USD (DollarColumn,
        // TextualCurrencyCode, CurrencySymbolIsRemoved) and EUR (EurColumn).
        // GBP, JPY, and INR are unpinned. Of those three, GBP carries the
        // highest real-world relevance for 10-K filings: it's the dominant
        // foreign currency in cross-listed SEC filings (UK/London-listed
        // ADRs, FTSE 100 dual-listed companies, AstraZeneca, BP, GlaxoSmithKline,
        // Shell etc. all report some segments in pounds).
        //
        // Two risks this pin catches:
        //
        // 1) Map-shrinking regression: a refactor that "consolidates" the
        //    CurrencyMap to just USD+EUR (under the assumption that SEC
        //    filings are USD-dominant) would compile, pass every existing
        //    test, and silently fail to consolidate GBP/JPY/INR columns. The
        //    pound symbol would remain in the rendered table and no
        //    "All values are in British Pounds" note would appear.
        //
        // 2) Tuple-swap regression: each entry maps a code to a
        //    `(Symbol, Name)` tuple. A refactor that reorders fields, swaps
        //    columns, or copy-pastes a wrong (Symbol,Name) pair (e.g. GBP →
        //    ("£", "Euros")) would only surface when the human-readable name
        //    is asserted explicitly. Existing pins assert "US Dollars" and
        //    "Euros"; the other three names ("British Pounds", "Japanese Yen",
        //    "Indian Rupees") are untested. This pin asserts the FULL exit
        //    string ("All values are in British Pounds."), so a name-swap
        //    regression fails here.
        //
        // The complementary pins (JPY, INR) intentionally remain unpinned for
        // now — they'd duplicate the structural assertion this one makes. Pick
        // GBP as the highest-business-value representative of the
        // non-dollar/non-euro tail.
        var html =
            @"<html><body><table>
  <tr><td>£</td><td></td><td>1000</td></tr>
  <tr><td>£</td><td></td><td>2000</td></tr>
</table></body></html>";

        var doc = _parser.ParseDocument(html);

        _sut.Execute(doc);

        var note = doc.QuerySelector("table + p em");
        note.Should().NotBeNull();
        note.TextContent.Should().Be("All values are in British Pounds.");
    }

    [Fact]
    public void JpyTextualCodeColumnFollowedByEmptyColumn_AddsJapaneseYenNote()
    {
        // Sibling to the GBP pin. Existing GBP pin documents the rationale
        // for individual currency-arm coverage (map-shrink and tuple-swap
        // regressions). This pin extends the family with JPY — the second-
        // most-relevant non-USD/non-EUR currency for SEC-cross-listed
        // filings: Toyota, Sony, Honda, Mitsubishi UFJ and the bulk of
        // Japanese ADRs report subsidiary segments in yen. The JPY
        // mapping is `("¥", "Japanese Yen")`.
        //
        // JPY is structurally distinct from GBP in one specific way that
        // makes the test stronger as a separate pin rather than a Theory
        // case: the `¥` symbol (U+00A5 YEN SIGN) collides visually with
        // Chinese Yuan, but the CurrencyMap has only one ¥-keyed entry
        // (JPY). Pinning the SYMBOL path for JPY would not isolate this
        // arm against a refactor that adds CNY/RMB and inadvertently
        // reorders DetectCurrency's foreach. Use the TEXTUAL "JPY"
        // code instead — DetectCurrency's OR condition (`text.Contains(symbol)
        // || text.Contains(code)`) means an ISO code like "JPY" hits
        // ONLY through CurrencyMap["JPY"]. This pin exercises the
        // "JPY"→Japanese Yen mapping path explicitly and would fail
        // both:
        //   • A map-shrink that drops the JPY entry (CurrencyMap collapsed
        //     to USD+EUR+GBP because "Asia ADRs are a small share").
        //   • A tuple-name swap (e.g. `JPY => ("¥", "Yen")` truncating
        //     "Japanese Yen" to just "Yen" during a "normalize the names"
        //     pass). The full-string note assertion fails either way.
        //
        // The textual-code path also exercises the OR's right arm
        // independently of the symbol arm — a complementary pin to the
        // existing USD test that already uses "USD" textual code.
        var html =
            @"<html><body><table>
  <tr><td>JPY</td><td></td><td>10000</td></tr>
  <tr><td>JPY</td><td></td><td>20000</td></tr>
</table></body></html>";

        var doc = _parser.ParseDocument(html);

        _sut.Execute(doc);

        var note = doc.QuerySelector("table + p em");
        note.Should().NotBeNull();
        note.TextContent.Should().Be("All values are in Japanese Yen.");
    }

    [Fact]
    public void InrSymbolColumnFollowedByEmptyColumn_AddsIndianRupeesNote()
    {
        // Third sibling in the per-currency-arm family (USD existing, EUR
        // existing, GBP/JPY pinned in this loop). Completes the
        // CurrencyMap entry pinning with INR. The mapping is
        // `("₹", "Indian Rupees")`.
        //
        // INR is the lowest-volume entry in CurrencyMap and the most
        // likely casualty of a "shrink the map" refactor: Indian ADRs
        // (Infosys, Wipro, ICICI Bank, HDFC, Reliance ADR) DO file with
        // SEC and report subsidiary segments in rupees, but the
        // frequency is lower than EUR/GBP/JPY. A refactor that
        // "rationalizes" CurrencyMap to the top 4 — silently dropping
        // INR — would compile, pass every existing pin (USD, EUR, GBP,
        // JPY), and silently fail to consolidate rupee columns. The
        // ₹ glyph would survive in the rendered table and no "All
        // values are in Indian Rupees" note would appear below it.
        //
        // The INR symbol `₹` (U+20B9 INDIAN RUPEE SIGN) is structurally
        // distinct from the other currency glyphs in the map: it's a
        // 3-byte UTF-8 codepoint that can lose its encoding on a
        // refactor that touches CurrencyMap construction (e.g.
        // re-emitting as a const string with a stripped escape) or
        // round-trips through a non-UTF-8 source path. Pinning the
        // symbol-key entry — rather than the textual "INR" code path
        // already exercised by the USD/JPY textual-code variants —
        // catches BOTH the entry-drop regression AND the
        // codepoint-mangling regression.
        //
        // Pair the symbol-path assertion (this pin) with the existing
        // GBP symbol-path pin and JPY textual-code pin to provide
        // diverse-mechanism coverage across the non-USD/non-EUR tail:
        //   - GBP via `£` symbol (single-byte ASCII-adjacent codepoint)
        //   - JPY via `JPY` textual code (3-letter ISO branch)
        //   - INR via `₹` symbol (3-byte UTF-8 codepoint)
        // Any single-pattern simplification refactor fails at least one.
        var html =
            @"<html><body><table>
  <tr><td>₹</td><td></td><td>50000</td></tr>
  <tr><td>₹</td><td></td><td>75000</td></tr>
</table></body></html>";

        var doc = _parser.ParseDocument(html);

        _sut.Execute(doc);

        var note = doc.QuerySelector("table + p em");
        note.Should().NotBeNull();
        note.TextContent.Should().Be("All values are in Indian Rupees.");
    }

    [Fact]
    public void CurrencySymbolIsRemovedFromConsolidatedText()
    {
        var html =
            @"<html><body><table>
  <tr><td>$</td><td></td><td>100</td></tr>
</table></body></html>";

        var doc = _parser.ParseDocument(html);

        _sut.Execute(doc);

        var allCellTexts = doc.QuerySelectorAll("td").Select(c => c.TextContent).ToList();

        allCellTexts.Should().NotContain(t => t.Contains("$"));
    }
}
