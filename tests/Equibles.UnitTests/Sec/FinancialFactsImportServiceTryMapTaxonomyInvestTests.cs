using System.Reflection;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.HostedService.Services;

namespace Equibles.UnitTests.Sec;

public class FinancialFactsImportServiceTryMapTaxonomyInvestTests
{
    // Completes the TryMapTaxonomy arm-set pin (us-gaap implicit / dei /
    // ifrs-full / srt / null already covered). `invest` is SEC's Investment-
    // Company Reporting (ICR) taxonomy — concepts unique to N-PORT / N-CEN
    // / N-2 filings (`invest:InvestmentCompanyName`, `invest:NumberOfClasses`).
    // A refactor that pruned this arm would silently drop every investment-
    // company-specific fact, leaving N-PORT panels with only the us-gaap
    // financials but none of the ICR-only structured disclosures.
    [Fact]
    public void TryMapTaxonomy_InvestKey_ReturnsTrueWithInvestTaxonomy()
    {
        var method = typeof(FinancialFactsImportService).GetMethod(
            "TryMapTaxonomy",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        var args = new object[] { "invest", default(FactTaxonomy) };

        var resolved = (bool)method.Invoke(null, args);

        resolved.Should().BeTrue();
        args[1].Should().Be(FactTaxonomy.Invest);
    }
}
