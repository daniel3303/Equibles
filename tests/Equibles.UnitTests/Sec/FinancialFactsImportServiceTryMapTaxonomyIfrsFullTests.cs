using System.Reflection;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.HostedService.Services;

namespace Equibles.UnitTests.Sec;

public class FinancialFactsImportServiceTryMapTaxonomyIfrsFullTests
{
    // TryMapTaxonomy maps SEC's `facts` JSON top-level taxonomy keys to the
    // FactTaxonomy enum. Sibling pins cover the Dei arm and the null-default
    // arm; us-gaap is implicitly exercised by integration tests. The
    // `ifrs-full` arm is the foreign-issuer (20-F) cohort — without it,
    // every foreign-private-issuer fact would drop to the default arm and
    // vanish from the FinancialFact stream. A refactor that "tightened" the
    // taxonomy set to just {us-gaap, dei} (an "we only persist US issuers"
    // simplification) would silently exclude every IFRS filer's data —
    // visible only as a Codecov delta and missing data on 20-F company
    // profile pages.
    [Fact]
    public void TryMapTaxonomy_IfrsFullKey_ReturnsTrueWithIfrsFullTaxonomy()
    {
        var method = typeof(FinancialFactsImportService).GetMethod(
            "TryMapTaxonomy",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        var args = new object[] { "ifrs-full", default(FactTaxonomy) };

        var resolved = (bool)method.Invoke(null, args);

        resolved.Should().BeTrue();
        args[1].Should().Be(FactTaxonomy.IfrsFull);
    }
}
