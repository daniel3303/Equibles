using System.Reflection;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.HostedService.Services;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Sibling to <see cref="FinancialFactsImportServiceTryMapFiscalPeriodUnknownTests"/>.
/// The Unknown pin protects the default arm; the discriminating arms (FY,
/// Q1–Q4) are unpinned. <c>FY</c> is asymmetric: it maps to
/// <c>SecFiscalPeriod.FullYear</c>, not <c>FY</c> or <c>Annual</c>, and is the
/// FIRST switch arm (the most likely casualty of a copy-paste-and-edit
/// insertion). A silent break of FY→FullYear (e.g. arm body retyped to Q1)
/// would classify every annual financial fact as Q1, corrupting the entire
/// FullYear column of the SEC Company Facts ingestion.
/// </summary>
public class FinancialFactsImportServiceTryMapFiscalPeriodFyTests
{
    [Fact]
    public void TryMapFiscalPeriod_FyToken_ReturnsTrueWithFullYearOut()
    {
        var method = typeof(FinancialFactsImportService).GetMethod(
            "TryMapFiscalPeriod",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        var args = new object[] { "FY", default(SecFiscalPeriod) };

        var resolved = (bool)method.Invoke(null, args);

        resolved.Should().BeTrue();
        args[1].Should().Be(SecFiscalPeriod.FullYear);
    }
}
