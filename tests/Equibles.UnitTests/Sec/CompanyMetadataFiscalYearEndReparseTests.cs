using Equibles.Integrations.Sec.Models;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Adversarial sibling to <see cref="CompanyMetadataFiscalYearEndTests"/>,
/// which only parses a single fixed value. The parse is memoised, but the
/// contract is explicit: "this is a deserialisation DTO" and "a re-set of
/// FiscalYearEnd re-parses". A stale memo would make a re-bound DTO report the
/// previous filer's fiscal year-end — corrupting every downstream quarter calc.
/// </summary>
public class CompanyMetadataFiscalYearEndReparseTests
{
    [Fact]
    public void FiscalYearEnd_ReSetAfterRead_ReparsesToNewValue()
    {
        var metadata = new CompanyMetadata { FiscalYearEnd = "0928" };

        // Read first so the parse is memoised against "0928" (month 9, day 28).
        _ = metadata.FiscalYearEndMonth;

        metadata.FiscalYearEnd = "0630";

        metadata.FiscalYearEndMonth.Should().Be(6);
        metadata.FiscalYearEndDay.Should().Be(30);
    }
}
