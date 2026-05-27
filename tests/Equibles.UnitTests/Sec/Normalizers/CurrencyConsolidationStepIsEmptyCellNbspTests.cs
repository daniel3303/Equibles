using System.Reflection;
using Equibles.Sec.BusinessLogic.Normalizers;

namespace Equibles.UnitTests.Sec.Normalizers;

public class CurrencyConsolidationStepIsEmptyCellNbspTests
{
    // IsEmptyCell is the guard inside ProcessCurrencyColumnsForConsolidation
    // that decides whether the NEXT cell is empty enough to accept the
    // current cell's content. The body is:
    //   return string.IsNullOrWhiteSpace(cellText) || cellText == " ";
    // The `|| == " "` arm looks redundant — `IsNullOrWhiteSpace`
    // already treats U+00A0 (NBSP) as whitespace via char.IsWhiteSpace —
    // but it's load-bearing under one specific regression class: a
    // "tighten" refactor that swaps `IsNullOrWhiteSpace` for
    // `IsNullOrEmpty`. SEC EDGAR's HTML emits NBSP-only cells routinely
    // (CKEditor's "make this cell visually empty" convention), so the
    // tightened predicate would silently misclassify every NBSP-padded
    // empty cell, blocking the currency-column consolidation pass and
    // leaving the $ / € / £ column un-folded in every affected table.
    //
    // No existing CurrencyConsolidationStep sibling exercises
    // IsEmptyCell with NBSP directly — Execute-level tests cover the
    // "happy path" empty cells (space-only or truly empty).
    //
    // Pin: IsEmptyCell(" ") returns true. The pin survives an
    // `IsNullOrWhiteSpace → IsNullOrEmpty` tightening because the
    // `cellText == " "` arm still fires; a regression that
    // drops BOTH guards (`return false;`) would surface here.
    [Fact]
    public void IsEmptyCell_NbspOnly_ReturnsTrue()
    {
        var method = typeof(CurrencyConsolidationStep).GetMethod(
            "IsEmptyCell",
            BindingFlags.NonPublic | BindingFlags.Instance
        );
        var step = new CurrencyConsolidationStep();

        var result = (bool)method.Invoke(step, [" "]);

        result.Should().BeTrue();
    }
}
