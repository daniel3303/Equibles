using System.Reflection;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.HostedService.Services;

namespace Equibles.UnitTests.Sec;

public class FinancialFactsImportServiceTryMapTaxonomySrtTests
{
    // Sibling to the us-gaap (implicit) / dei / ifrs-full / null pins.
    // SRT is FASB's Standard Reference Taxonomy — used for SEC Schedule-of-
    // Investments concepts (`srt:Symbol`, `srt:NumberOfDirectors`,
    // `srt:CounterpartyNameAxis`) that appear on every fund and BDC filing.
    // Without this arm, every fund holdings disclosure would drop to the
    // default arm and silently vanish. A refactor that pruned the SRT arm
    // ("we only persist company-fact-table data") would compile and leave
    // investment-company panels with zero rows.
    [Fact]
    public void TryMapTaxonomy_SrtKey_ReturnsTrueWithSrtTaxonomy()
    {
        var method = typeof(FinancialFactsImportService).GetMethod(
            "TryMapTaxonomy",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        var args = new object[] { "srt", default(FactTaxonomy) };

        var resolved = (bool)method.Invoke(null, args);

        resolved.Should().BeTrue();
        args[1].Should().Be(FactTaxonomy.Srt);
    }
}
