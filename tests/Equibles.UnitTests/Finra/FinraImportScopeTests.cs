using Equibles.Finra.HostedService.Services;

namespace Equibles.UnitTests.Finra;

public class FinraImportScopeTests
{
    [Fact]
    public void Resolve_EmptyOrBlankTickerSet_UsesAllScope()
    {
        FinraImportScope.Resolve([]).Should().Be("all");
        FinraImportScope.Resolve(["", "  "]).Should().Be("all");
    }

    [Fact]
    public void Resolve_SameTickersWithDifferentCaseOrderAndWhitespace_UsesSameScope()
    {
        var canonical = FinraImportScope.Resolve(["AAPL", "MSFT"]);

        var reordered = FinraImportScope.Resolve([" msft ", "aapl", "AAPL", ""]);

        reordered.Should().Be(canonical);
        canonical.Should().StartWith("tickers:");
    }

    [Fact]
    public void Resolve_DifferentTickerSet_UsesDifferentScope()
    {
        FinraImportScope.Resolve(["AAPL"]).Should().NotBe(FinraImportScope.Resolve(["MSFT"]));
    }
}
