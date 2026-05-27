using System.Reflection;
using Equibles.CommonStocks.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.HostedService.Services;

namespace Equibles.UnitTests.Sec;

public class FinancialFactsImportServiceBuildFactMissingDocumentIdTests
{
    [Fact]
    public void BuildFact_AccessionNotInDocumentIdsMap_DocumentIdIsNullNotGuidEmpty()
    {
        // Contract (BuildFact line 359): `documentIds.TryGetValue(p.Accession, out
        // var docId) ? docId : null`. A fact can be imported before its document
        // is scraped — the accession won't yet be in the documentIds map. The
        // ternary's miss arm produces a Guid? null. A refactor to the seemingly
        // equivalent `documentIds.GetValueOrDefault(p.Accession)` would return
        // `default(Guid) == Guid.Empty`, which assigns to the `Guid?` column as
        // Guid.Empty (NOT null), corrupting FK references and the unique
        // (CommonStockId,FinancialConceptId,...,AccessionNumber) index. Existing
        // BuildFact pins cover missing-concept and unknown-form arms; neither
        // asserts on DocumentId. Pin: empty documentIds map → DocumentId null.
        var serviceType = typeof(FinancialFactsImportService);
        var parsedFactType = serviceType.GetNestedType("ParsedFact", BindingFlags.NonPublic);
        var parsedFact = Activator.CreateInstance(parsedFactType);
        SetInit(parsedFact, "Taxonomy", FactTaxonomy.UsGaap);
        SetInit(parsedFact, "Tag", "Revenues");
        SetInit(parsedFact, "Unit", "USD");
        SetInit(parsedFact, "PeriodType", FactPeriodType.Duration);
        SetInit(parsedFact, "PeriodStart", new DateOnly(2024, 1, 1));
        SetInit(parsedFact, "PeriodEnd", new DateOnly(2024, 12, 31));
        SetInit(parsedFact, "Value", 100m);
        SetInit(parsedFact, "FiscalYear", 2024);
        SetInit(parsedFact, "FiscalPeriod", SecFiscalPeriod.FullYear);
        SetInit(parsedFact, "Form", "10-K");
        SetInit(parsedFact, "Filed", new DateOnly(2025, 1, 15));
        SetInit(parsedFact, "Accession", "0001234567-25-000001");

        var stock = new CommonStock { Id = Guid.NewGuid() };
        var conceptIds = new Dictionary<(FactTaxonomy, string), Guid>
        {
            [(FactTaxonomy.UsGaap, "Revenues")] = Guid.NewGuid(),
        };
        var documentIds = new Dictionary<string, Guid>();

        var method = serviceType.GetMethod(
            "BuildFact",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        var fact = method!.Invoke(null, [stock, parsedFact, conceptIds, documentIds]);

        var documentId = (Guid?)fact!.GetType().GetProperty("DocumentId")!.GetValue(fact);
        documentId.Should().BeNull();
    }

    private static void SetInit(object target, string property, object value) =>
        target!.GetType().GetProperty(property)!.SetValue(target, value);
}
