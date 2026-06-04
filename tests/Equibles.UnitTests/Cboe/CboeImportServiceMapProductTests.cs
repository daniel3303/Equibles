using System.Reflection;
using Equibles.Cboe.HostedService.Services;
using Equibles.Integrations.Cboe.Models;

namespace Equibles.UnitTests.Cboe;

public class CboeImportServiceMapProductTests
{
    // MapProduct maps each known CBOE put/call product to its persisted ratio type. An undefined
    // product value must fail fast with ArgumentOutOfRangeException, never silently mis-map (e.g. a
    // new product added to the source enum but not here). The Import tests only exercise the known
    // products; this pins the catch-all throw. Oracle from the out-of-range-enum contract.
    [Fact]
    public void MapProduct_UndefinedProduct_ThrowsArgumentOutOfRange()
    {
        var method = typeof(CboeImportService).GetMethod(
            "MapProduct",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var act = () => method.Invoke(null, [(CboePutCallProductType)999]);

        act.Should()
            .Throw<TargetInvocationException>()
            .WithInnerException<ArgumentOutOfRangeException>();
    }
}
