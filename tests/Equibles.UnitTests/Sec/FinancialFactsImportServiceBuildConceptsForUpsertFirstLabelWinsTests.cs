using System.Collections;
using System.Reflection;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.HostedService.Services;

namespace Equibles.UnitTests.Sec;

public class FinancialFactsImportServiceBuildConceptsForUpsertFirstLabelWinsTests
{
    [Fact]
    public void BuildConceptsForUpsert_DuplicateTagWithDifferentLabels_KeepsFirstEncounteredLabel()
    {
        // BuildConceptsForUpsert (extracted in #1500) collapses a list of
        // ParsedFact rows into the unique-(Taxonomy, Tag) pairs the
        // FinancialConcept upsert needs. SEC frequently emits the SAME
        // (taxonomy, tag) across many filings with slightly varying
        // human-readable Labels (capitalisation drift, phrasing tweaks
        // between fiscal years, or sub-form variants). The contract per
        // the implementation's `g.First().Label` is "first-encountered
        // label wins" — the iteration order through `parsed` is the
        // tiebreak.
        //
        // The risk this catches: a refactor that "modernises" the helper
        // to `g.Last().Label`, or simply re-orders the GroupBy in a way
        // that loses iteration stability (e.g. through an
        // .OrderBy(x => x.Tag) inserted ahead of the group), would
        // compile, pass any test that feeds unique (Tax, Tag) pairs,
        // and silently flip every concept's stored Label on the first
        // import that contains a same-pair duplicate. The Label is
        // user-visible in MCP responses and analyst views — flipping
        // it mid-import is a confusing, hard-to-attribute regression.
        //
        // Pin: two ParsedFact rows with the SAME (Taxonomy, Tag) but
        // different Labels. The resulting FinancialConcept must carry
        // the FIRST label.
        var serviceType = typeof(FinancialFactsImportService);
        var parsedFactType = serviceType.GetNestedType("ParsedFact", BindingFlags.NonPublic);

        var first = Activator.CreateInstance(parsedFactType);
        parsedFactType.GetProperty("Taxonomy").SetValue(first, FactTaxonomy.UsGaap);
        parsedFactType.GetProperty("Tag").SetValue(first, "Revenues");
        parsedFactType.GetProperty("Label").SetValue(first, "Revenues — First Label");

        var second = Activator.CreateInstance(parsedFactType);
        parsedFactType.GetProperty("Taxonomy").SetValue(second, FactTaxonomy.UsGaap);
        parsedFactType.GetProperty("Tag").SetValue(second, "Revenues");
        parsedFactType.GetProperty("Label").SetValue(second, "Revenues — Second Label");

        var listType = typeof(List<>).MakeGenericType(parsedFactType);
        var list = (IList)Activator.CreateInstance(listType);
        list.Add(first);
        list.Add(second);

        var method = serviceType.GetMethod(
            "BuildConceptsForUpsert",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var result = method.Invoke(null, [list]);

        // Result is a ValueTuple<HashSet<(FactTaxonomy, string)>, List<FinancialConcept>>.
        var concepts = (List<FinancialConcept>)result.GetType().GetField("Item2").GetValue(result);

        concepts.Should().ContainSingle();
        concepts[0].Label.Should().Be("Revenues — First Label");
    }
}
