using Equibles.Finra.HostedService.Services;
using Equibles.CommonStocks.Data.Models;

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

    [Fact]
    public void ResolveStockUniverse_SameStocksInDifferentOrder_UsesSameScope()
    {
        var appleId = Guid.NewGuid();
        var microsoftId = Guid.NewGuid();
        var canonical = new Dictionary<string, Guid>(StringComparer.Ordinal)
        {
            ["AAPL"] = appleId,
            ["MSFT"] = microsoftId,
        };
        var reordered = new Dictionary<string, Guid>(StringComparer.Ordinal)
        {
            ["MSFT"] = microsoftId,
            ["AAPL"] = appleId,
        };

        FinraImportScope
            .ResolveStockUniverse(reordered)
            .Should()
            .Be(FinraImportScope.ResolveStockUniverse(canonical))
            .And.StartWith("stocks:");
    }

    [Fact]
    public void ResolveStockUniverse_AddedOrReplacedStock_UsesDifferentScope()
    {
        var appleId = Guid.NewGuid();
        var original = new Dictionary<string, Guid>(StringComparer.Ordinal) { ["AAPL"] = appleId };
        var added = new Dictionary<string, Guid>(StringComparer.Ordinal)
        {
            ["AAPL"] = appleId,
            ["MSFT"] = Guid.NewGuid(),
        };
        var replaced = new Dictionary<string, Guid>(StringComparer.Ordinal)
        {
            ["AAPL"] = Guid.NewGuid(),
        };

        var originalScope = FinraImportScope.ResolveStockUniverse(original);
        FinraImportScope.ResolveStockUniverse(added).Should().NotBe(originalScope);
        FinraImportScope.ResolveStockUniverse(replaced).Should().NotBe(originalScope);
    }

    [Fact]
    public void ResolveStockUniverse_ExactTickerCasing_IsPartOfIdentity()
    {
        var stockId = Guid.NewGuid();
        var common = new Dictionary<string, Guid>(StringComparer.Ordinal) { ["TPC"] = stockId };
        var preferred = new Dictionary<string, Guid>(StringComparer.Ordinal) { ["TpC"] = stockId };

        FinraImportScope
            .ResolveStockUniverse(preferred)
            .Should()
            .NotBe(FinraImportScope.ResolveStockUniverse(common));
    }

    [Fact]
    public void ResolveListingImportScope_UnfilteredUniverse_UsesCompletenessMarker()
    {
        var listings = new Dictionary<string, ListedSecurityKey>(StringComparer.Ordinal)
        {
            ["AAPL"] = new(Guid.NewGuid(), "AAPL"),
        };

        FinraImportScope.ResolveListingImportScope(listings, []).Should().StartWith("listings:");
        FinraImportScope
            .ResolveListingImportScope(listings, ["", "  "])
            .Should()
            .StartWith("listings:");
    }

    [Fact]
    public void ResolveListingImportScope_ConfiguredSubset_UsesSubsetMarker()
    {
        var listings = new Dictionary<string, ListedSecurityKey>(StringComparer.Ordinal)
        {
            ["VOO"] = new(Guid.NewGuid(), "VOO"),
        };

        var scope = FinraImportScope.ResolveListingImportScope(listings, [" voo "]);

        scope.Should().StartWith("listing-filter:");
    }

    [Fact]
    public void ResolveListingImportScope_ConfiguredSubsetChangesWhenResolutionChanges()
    {
        var stockId = Guid.NewGuid();
        var unresolved = new Dictionary<string, ListedSecurityKey>(StringComparer.Ordinal);
        var resolved = new Dictionary<string, ListedSecurityKey>(StringComparer.Ordinal)
        {
            ["VOO"] = new(stockId, "VOO"),
        };
        var reassigned = new Dictionary<string, ListedSecurityKey>(StringComparer.Ordinal)
        {
            ["VOO"] = new(Guid.NewGuid(), "VOO"),
        };

        var unresolvedScope = FinraImportScope.ResolveListingImportScope(unresolved, ["VOO"]);
        var resolvedScope = FinraImportScope.ResolveListingImportScope(resolved, ["VOO"]);

        resolvedScope.Should().NotBe(unresolvedScope);
        FinraImportScope
            .ResolveListingImportScope(reassigned, ["VOO"])
            .Should()
            .NotBe(resolvedScope);
    }
}
