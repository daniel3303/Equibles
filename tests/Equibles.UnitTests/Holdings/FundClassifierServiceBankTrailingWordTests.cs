using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

/// <summary>
/// The "BANK " rule (trailing space) won't match holder names where "BANK"
/// is the final word. Real 13F filers like "Deutsche Bank" are misclassified
/// as Unknown because the name contains no character after "BANK".
/// </summary>
public class FundClassifierServiceBankTrailingWordTests
{
    [Fact]
    public void Classify_BankAsLastWord_ReturnsBank()
    {
        FundClassifierService.Classify("Deutsche Bank").Should().Be(FundClassification.Bank);
    }
}
