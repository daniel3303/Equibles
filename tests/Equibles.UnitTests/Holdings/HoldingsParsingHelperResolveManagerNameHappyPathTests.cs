using Equibles.Holdings.HostedService.Models;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

public class HoldingsParsingHelperResolveManagerNameHappyPathTests
{
    [Fact]
    public void ResolveManagerName_AccessionAndManagerNumberPresent_ReturnsName()
    {
        // Completes the ResolveManagerName coverage by pinning the only path that
        // returns a non-null value. The sibling test
        // (ResolveManagerName_AccessionNotInOtherManagers_ReturnsNull) pins the
        // null-fallback; without this happy-path pin, a regression that always
        // returned null (e.g. a refactor that inverted the TryGetValue success
        // check) would pass both the missing-accession test and the
        // managerNumber-null guard test, while silently NULLING every co-filer's
        // name in every multi-manager 13F-HR filing.
        var context = new ImportContext
        {
            OtherManagers = new Dictionary<string, Dictionary<int, string>>
            {
                ["0001234-25-000001"] = new Dictionary<int, string>
                {
                    [2] = "Acme Capital Advisors",
                },
            },
        };

        var result = HoldingsParsingHelper.ResolveManagerName(
            context,
            "0001234-25-000001",
            managerNumber: 2
        );

        result.Should().Be("Acme Capital Advisors");
    }
}
