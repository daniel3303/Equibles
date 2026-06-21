using Equibles.CommonStocks.HostedService.Services;

namespace Equibles.UnitTests.CommonStocks;

public class InvestorRelationsCandidateBuilderCaseInsensitiveDedupTests
{
    // Contract: results are de-duplicated CASE-INSENSITIVELY while preserving order. Two path
    // segments differing only in case ("IR" then "ir") describe the same probe target, so only
    // the first survives — order-preserving dedup keeps the earlier candidate with its casing.
    [Fact]
    public void Build_PathSegmentsDifferingOnlyByCase_DedupesKeepingTheFirst()
    {
        var result = InvestorRelationsCandidateBuilder.Build("https://acme.com", ["IR", "ir"], []);

        result.Should().Equal("https://acme.com/IR");
    }
}
