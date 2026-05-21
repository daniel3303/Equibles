using System.Reflection;
using Equibles.CommonStocks.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.HostedService.Services;

namespace Equibles.UnitTests.Sec;

public class FinancialFactsImportServiceBuildFactMissingConceptTests
{
    // BuildFact's pre-pipeline fail-soft contract: if the (Taxonomy, Tag) of
    // the parsed fact is not present in the resolved conceptIds dictionary the
    // helper returns null instead of throwing, so the caller can quietly drop
    // the orphan row. Concept resolution races a fresh DB (the FinancialConcept
    // upsert and the subsequent fact insert use different scopes), so a missing
    // entry is a real, transient possibility. A refactor that swapped TryGetValue
    // for an indexer lookup would compile and start throwing KeyNotFoundException
    // mid-import, aborting the entire batch instead of skipping the one row.
    [Fact]
    public void BuildFact_TaxonomyTagNotInConceptIds_ReturnsNull()
    {
        var serviceType = typeof(FinancialFactsImportService);
        var parsedFactType = serviceType.GetNestedType("ParsedFact", BindingFlags.NonPublic);

        var parsed = Activator.CreateInstance(parsedFactType);
        parsedFactType.GetProperty("Taxonomy").SetValue(parsed, FactTaxonomy.UsGaap);
        parsedFactType.GetProperty("Tag").SetValue(parsed, "UnknownTagNotInMap");
        parsedFactType.GetProperty("Accession").SetValue(parsed, "0000320193-24-000123");
        parsedFactType.GetProperty("Form").SetValue(parsed, "10-K");

        var stock = new CommonStock { Id = Guid.NewGuid() };
        var conceptIds = new Dictionary<(FactTaxonomy, string), Guid>
        {
            { (FactTaxonomy.UsGaap, "Revenues"), Guid.NewGuid() },
        };
        var documentIds = new Dictionary<string, Guid>();

        var method = serviceType.GetMethod(
            "BuildFact",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var result = (FinancialFact)method.Invoke(null, [stock, parsed, conceptIds, documentIds]);

        result.Should().BeNull();
    }
}
