using System.Reflection;
using Equibles.Sec.Mcp.Tools;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Tests for <see cref="DocumentTextTools"/>. The public MCP tools hit the database
/// for document content, so we exercise the pure-logic private static helpers via
/// reflection — the same pattern used by YahooFinanceClientTests for its private
/// static ToUnixTimestamp / FromUnixTimestamp helpers.
/// </summary>
public class DocumentTextToolsTests {
    private static readonly MethodInfo HighlightKeywordMethod = typeof(DocumentTextTools)
        .GetMethod("HighlightKeyword", BindingFlags.NonPublic | BindingFlags.Static);

    [Fact]
    public void HighlightKeyword_KeywordOccursWithDifferentCasing_HighlightsAllOccurrencesPreservingOriginalCase() {
        // SearchDocumentKeyword is the MCP tool's keyword-in-document search entry
        // point: it returns snippet lines with every matched keyword wrapped in
        // markdown bold (`**...**`) so the LLM consumer can see WHICH word in a
        // returned chunk actually matched. The wrapping is done by HighlightKeyword,
        // which uses `String.IndexOf(..., StringComparison.OrdinalIgnoreCase)` so a
        // lowercase user query ("revenue") matches a document occurrence with ANY
        // casing ("Revenue", "REVENUE", "revenue"). This is the universal SEC-
        // document case: 10-K and 10-Q filings use Title Case for section headers
        // ("Total Revenue") and Sentence Case for prose ("our revenue"), so every
        // realistic LLM query against a real filing depends on case-insensitive
        // matching to find ALL occurrences in one shot.
        //
        // The risk this pins: a regression that swapped `OrdinalIgnoreCase` for the
        // case-sensitive default `Ordinal` (a single-character delete, easy to
        // happen during a "modernize StringComparison usage" cleanup) would
        // silently break highlighting on every mixed-case occurrence. The MCP
        // tool would still RETURN the snippets — the find-and-return loop above
        // it uses a different code path — but the LLM consumer would lose the
        // signal that tells it which word matched in each snippet, because only
        // the verbatim-case occurrences would be wrapped. With Title Case being
        // the dominant style in SEC filings, that means most highlights would
        // silently disappear. Critically, the bug is INVISIBLE: a search for
        // "revenue" against a 10-K returns the right snippets, just without the
        // `**Revenue**` markers in them — no exception, no log, no CI signal.
        // Operators discover it months later when LLM citation accuracy drops.
        //
        // Construction: a line with three occurrences of "revenue" in three
        // different casings (Title, lower, ALL CAPS), interleaved with non-match
        // text. The assertion checks ALL three are wrapped AND the original
        // character casing is preserved inside the `**...**` (the implementation
        // does `result.Append(line, matchIndex, keyword.Length)` — i.e. it
        // copies from `line`, not from `keyword`, so a regression that
        // accidentally appended `keyword` instead would output `**revenue**`
        // three times and lose the visual distinction that the LLM can see).
        var result = (string)HighlightKeywordMethod.Invoke(null,
            ["Total Revenue increased; quarterly revenue beat estimates; REVENUE outlook stable.", "revenue"]);

        result.Should().Be("Total **Revenue** increased; quarterly **revenue** beat estimates; **REVENUE** outlook stable.");
    }
}
