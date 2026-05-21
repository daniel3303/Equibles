using System.Reflection;
using Equibles.CommonStocks.Data.Models;
using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.HostedService.Services;

namespace Equibles.UnitTests.Sec;

public class FinancialFactsImportServiceBuildFactUnknownFormTests
{
    // BuildFact's WHY-comment is explicit: "SEC emits many form strings outside
    // the known DocumentTypes (NT 10-K, S-1, 485BPOS, …); fold them into Other
    // rather than fabricating untracked DocumentType instances." The contract
    // is the `?? DocumentType.Other` fallback. A refactor that dropped the
    // null-coalesce (or that pre-validated the form against a known list and
    // skipped the row instead) would leave Form null and crash any downstream
    // consumer that dereferences `fact.Form.DisplayName`.
    [Fact]
    public void BuildFact_ParsedFactWithUnknownForm_FoldsFormToDocumentTypeOther()
    {
        var serviceType = typeof(FinancialFactsImportService);
        var parsedFactType = serviceType.GetNestedType("ParsedFact", BindingFlags.NonPublic);
        var parsedFact = Activator.CreateInstance(parsedFactType);
        SetInit(parsedFact, "Taxonomy", FactTaxonomy.UsGaap);
        SetInit(parsedFact, "Tag", "Revenues");
        SetInit(parsedFact, "Unit", "USD");
        SetInit(parsedFact, "PeriodType", FactPeriodType.Duration);
        SetInit(parsedFact, "PeriodStart", new DateOnly(2024, 1, 1));
        SetInit(parsedFact, "PeriodEnd", new DateOnly(2024, 12, 31));
        SetInit(parsedFact, "Value", 1_000_000m);
        SetInit(parsedFact, "FiscalYear", 2024);
        SetInit(parsedFact, "FiscalPeriod", SecFiscalPeriod.FullYear);
        SetInit(parsedFact, "Form", "NT 10-K");
        SetInit(parsedFact, "Filed", new DateOnly(2025, 2, 1));
        SetInit(parsedFact, "Accession", "0001234567-25-000001");

        var stock = new CommonStock { Id = Guid.NewGuid() };
        var conceptId = Guid.NewGuid();
        var conceptIds = new Dictionary<(FactTaxonomy, string), Guid>
        {
            [(FactTaxonomy.UsGaap, "Revenues")] = conceptId,
        };
        var documentIds = new Dictionary<string, Guid>();

        var method = serviceType.GetMethod(
            "BuildFact",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var fact = method.Invoke(null, [stock, parsedFact, conceptIds, documentIds]);

        var form = fact.GetType().GetProperty("Form").GetValue(fact);
        form.Should().Be(DocumentType.Other);
    }

    private static void SetInit(object target, string property, object value) =>
        target.GetType().GetProperty(property).SetValue(target, value);
}
