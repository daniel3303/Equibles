using Equibles.CommonStocks.HostedService.Services;

namespace Equibles.UnitTests.CommonStocks;

public class InvestorRelationsCandidateBuilderTests
{
    private static readonly string[] Paths = ["investor-relations", "ir"];
    private static readonly string[] Subdomains = ["ir", "investors"];

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Build_MissingWebsite_ReturnsEmpty(string website)
    {
        // Contract: a stock with no website has nothing to probe against.
        InvestorRelationsCandidateBuilder.Build(website, Paths, Subdomains).Should().BeEmpty();
    }

    [Fact]
    public void Build_UnparseableWebsite_ReturnsEmpty()
    {
        // "not a url" prepends https:// but still contains a space, so Uri rejects it.
        InvestorRelationsCandidateBuilder.Build("not a url", Paths, Subdomains).Should().BeEmpty();
    }

    [Fact]
    public void Build_SubdomainsPrecedePaths_AndUseRegistrableDomainForSubdomains()
    {
        // Contract: probe the IR subdomains of the registrable domain first (the
        // canonical investor portal), then the company website's own paths. The
        // "www." is stripped for subdomain hosts but kept for same-origin path probes.
        var result = InvestorRelationsCandidateBuilder.Build(
            "https://www.acme.com",
            Paths,
            Subdomains
        );

        result
            .Should()
            .Equal(
                "https://ir.acme.com",
                "https://investors.acme.com",
                "https://www.acme.com/investor-relations",
                "https://www.acme.com/ir"
            );
    }

    [Fact]
    public void Build_SchemeLessWebsite_AssumesHttps()
    {
        // EDGAR sometimes stores a bare host; it must still produce absolute URLs.
        var result = InvestorRelationsCandidateBuilder.Build("acme.com", Paths, Subdomains);

        result.Should().Contain("https://acme.com/investor-relations");
        result.Should().Contain("https://ir.acme.com");
    }

    [Fact]
    public void Build_PreservesHttpScheme()
    {
        var result = InvestorRelationsCandidateBuilder.Build("http://acme.com", ["ir"], ["ir"]);

        result.Should().Equal("http://ir.acme.com", "http://acme.com/ir");
    }

    [Fact]
    public void Build_DeduplicatesCandidatesPreservingOrder()
    {
        // Duplicate path entries must not yield duplicate probes.
        var result = InvestorRelationsCandidateBuilder.Build("https://acme.com", ["ir", "ir"], []);

        result.Should().Equal("https://acme.com/ir");
    }

    [Fact]
    public void Build_IgnoresBlankPathAndSubdomainEntries()
    {
        var result = InvestorRelationsCandidateBuilder.Build(
            "https://acme.com",
            ["", "  ", "ir"],
            [" "]
        );

        result.Should().Equal("https://acme.com/ir");
    }

    [Fact]
    public void Build_TrimsSlashesAndDotsFromSegments()
    {
        var result = InvestorRelationsCandidateBuilder.Build(
            "https://acme.com",
            ["/investors/"],
            [".ir."]
        );

        result.Should().Equal("https://ir.acme.com", "https://acme.com/investors");
    }
}
