using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Extensions;

namespace Equibles.UnitTests.Holdings;

public class HoldingsFilingTypeExtensionsAmendmentMapsToBaseFormTests
{
    [Fact]
    public void ToHoldingsFilingType_AmendmentFormType_MapsToSameTypeAsBaseForm()
    {
        // Contract (doc): the "/A" amendment marker is ignored — an amendment maps to
        // the same FilingType as its base form, so "13F-HR/A" must resolve to Form13F
        // exactly as the base "13F-HR" form does.
        var result = "13F-HR/A".ToHoldingsFilingType();

        result
            .Should()
            .Be(
                FilingType.Form13F,
                "the documented contract ignores the /A amendment marker, so an amended "
                    + "13F-HR resolves to the same type as the base 13F-HR form"
            );
    }
}
