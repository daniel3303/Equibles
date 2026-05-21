using System.Reflection;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.HostedService.Services;

namespace Equibles.UnitTests.Sec;

public class FinancialFactsImportServiceTryMapTaxonomyNullKeyTests
{
    // FinancialFactsImportService.TryMapTaxonomy follows the .NET Try* contract:
    // map the wire key to a FactTaxonomy via the out parameter and return true,
    // or return false on any input the helper does not recognize. The sibling
    // TryMapFiscalPeriod (same file, same shape) already defends against null
    // via `fp?.ToUpperInvariant()`; TryMapTaxonomy uses `key.ToLowerInvariant()`
    // without the null-conditional, so a null key throws NullReferenceException
    // instead of falling to the default arm. The Try* contract says "never
    // throw on bad input — return false" — any future caller (refactor,
    // parameterised matrix, defensive caller) that hands in null would crash.
    [Fact(
        Skip = "GH-1577 — TryMapTaxonomy throws NullReferenceException on null key instead of returning false"
    )]
    public void TryMapTaxonomy_NullKey_ReturnsFalseWithoutThrowing()
    {
        var method = typeof(FinancialFactsImportService).GetMethod(
            "TryMapTaxonomy",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        var args = new object[] { null, default(FactTaxonomy) };

        // MethodInfo.Invoke wraps a thrown inner exception in
        // TargetInvocationException, so `.NotThrow()` catches the NRE case.
        bool resolved = false;
        var act = () => resolved = (bool)method.Invoke(null, args);

        act.Should().NotThrow();
        resolved.Should().BeFalse();
    }
}
