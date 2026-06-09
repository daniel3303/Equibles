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
    public void Build_PathsPrecedeSubdomains_AndUseRegistrableDomainForSubdomains()
    {
        // Contract: probe the company website's own paths first (strongest match
        // when present), then IR subdomains of the registrable domain. The "www."
        // is kept for same-origin path probes but stripped for subdomain hosts.
        var result = InvestorRelationsCandidateBuilder.Build(
            "https://www.acme.com",
            Paths,
            Subdomains
        );

        result
            .Should()
            .Equal(
                "https://www.acme.com/investor-relations",
                "https://www.acme.com/ir",
                "https://ir.acme.com",
                "https://investors.acme.com"
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

        result.Should().Equal("http://acme.com/ir", "http://ir.acme.com");
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

        result.Should().Equal("https://acme.com/investors", "https://ir.acme.com");
    }
}
