using System.Reflection;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.HostedService.Services;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Sibling to FinancialFactsImportServiceTryMapTaxonomyNullKeyTests. The
/// null-key default-arm path is pinned. Of the five positive arms
/// (us-gaap / dei / ifrs-full / srt / invest), only us-gaap is implicitly
/// exercised by end-to-end import tests. The "dei" arm is independently
/// load-bearing — DEI (Document and Entity Information) facts ship with
/// every SEC XBRL filing for entity metadata (registrant name, CIK,
/// fiscal-year-end, filer category). A refactor that pared the switch back
/// to just `us-gaap` (intuitive: "we only persist us-gaap facts") would
/// compile, pass every other pin, and silently drop every DEI fact at
/// ingest — leaving the FinancialConcepts table without the entity-shape
/// rows downstream dashboards key off.
/// </summary>
public class FinancialFactsImportServiceTryMapTaxonomyDeiTests
{
    [Fact]
    public void TryMapTaxonomy_DeiKey_MapsToDeiTaxonomyAndReturnsTrue()
    {
        // The "dei" wire key (lowercase, matching SEC's submission JSON keys)
        // must round-trip into FactTaxonomy.Dei via the out parameter.
        var method = typeof(FinancialFactsImportService).GetMethod(
            "TryMapTaxonomy",
            BindingFlags.NonPublic | BindingFlags.Static
        )!;
        var args = new object[] { "dei", default(FactTaxonomy) };

        var resolved = (bool)method.Invoke(null, args)!;

        resolved.Should().BeTrue();
        args[1].Should().Be(FactTaxonomy.Dei);
    }
}
