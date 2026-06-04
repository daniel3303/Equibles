using System.Reflection;
using Equibles.Cboe.Data.Models;
using Equibles.Cboe.HostedService.Services;
using Equibles.Integrations.Cboe.Models;

namespace Equibles.UnitTests.Cboe;

public class CboeImportServiceMapProductKnownMappingsTests
{
    // MapProduct must route each known CBOE product to its SAME-named persisted ratio
    // type (Total→Total, Equity→Equity, Index→Index, Vix→Vix, Etp→Etp). The existing
    // pin only exercises the catch-all throw, so a swapped arm (e.g. Equity→Index) — the
    // exact "silent mis-map" that pin's comment warns about — would slip through. This
    // pins all five mappings; the multiple asserts verify one concept (the correspondence).
    [Fact]
    public void MapProduct_EachKnownProduct_MapsToSameNamedRatioType()
    {
        var method = typeof(CboeImportService).GetMethod(
            "MapProduct",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        CboePutCallRatioType Map(CboePutCallProductType p) =>
            (CboePutCallRatioType)method!.Invoke(null, [p])!;

        Map(CboePutCallProductType.Total).Should().Be(CboePutCallRatioType.Total);
        Map(CboePutCallProductType.Equity).Should().Be(CboePutCallRatioType.Equity);
        Map(CboePutCallProductType.Index).Should().Be(CboePutCallRatioType.Index);
        Map(CboePutCallProductType.Vix).Should().Be(CboePutCallRatioType.Vix);
        Map(CboePutCallProductType.Etp).Should().Be(CboePutCallRatioType.Etp);
    }
}
