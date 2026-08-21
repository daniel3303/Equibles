using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.HostedService.Services;

namespace Equibles.UnitTests.Sec;

public class FinancialFactsImportServiceFormTests
{
    [Theory]
    [InlineData("TwentyF", true)]
    [InlineData("TwentyFa", true)]
    [InlineData("FortyF", true)]
    [InlineData("FortyFa", true)]
    [InlineData("SixK", false)]
    [InlineData("SixKa", false)]
    public void IsAnnualPeriodicForm_ForeignForm_ReturnsExpected(string formValue, bool expected)
    {
        FinancialFactsImportService
            .IsAnnualPeriodicForm(DocumentType.FromValue(formValue))
            .Should()
            .Be(expected);
    }
}
