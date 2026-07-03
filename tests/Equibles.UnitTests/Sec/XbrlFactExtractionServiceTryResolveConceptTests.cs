using Equibles.Sec.FinancialFacts.BusinessLogic.Models;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.HostedService.Services;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// TryResolveConcept decides a parsed fact's persisted identity: the five
/// standard prefixes map to their enum arm with the tag untouched; everything
/// else is a filer-extension concept only when its namespace URI is owned by
/// neither a standards body nor the SEC. Ownership is the classifier — the
/// prefix spelling never is, so a company must not be misfiled because its
/// prefix happens to collide with a reference taxonomy and a reference
/// taxonomy must never leak into Custom.
/// </summary>
public class XbrlFactExtractionServiceTryResolveConceptTests
{
    private static ParsedXbrlFact Fact(string taxonomy, string tag, string ns) =>
        new()
        {
            Taxonomy = taxonomy,
            Tag = tag,
            Namespace = ns,
            Unit = "USD",
            Value = 1m,
            IsInstant = true,
            PeriodStart = new DateOnly(2025, 12, 27),
            PeriodEnd = new DateOnly(2025, 12, 27),
        };

    [Fact]
    public void TryResolveConcept_StandardPrefix_MapsWithRawTag()
    {
        var resolved = XbrlFactExtractionService.TryResolveConcept(
            Fact("us-gaap", "Revenues", "http://fasb.org/us-gaap/2023"),
            out var taxonomy,
            out var tag
        );

        resolved.Should().BeTrue();
        taxonomy.Should().Be(FactTaxonomy.UsGaap);
        tag.Should().Be("Revenues");
    }

    [Fact]
    public void TryResolveConcept_CompanyNamespace_MapsToCustomWithPrefixedTag()
    {
        var resolved = XbrlFactExtractionService.TryResolveConcept(
            Fact("adbe", "AnnualizedRecurringRevenue", "http://www.adobe.com/20231201"),
            out var taxonomy,
            out var tag
        );

        resolved.Should().BeTrue();
        taxonomy.Should().Be(FactTaxonomy.Custom);
        tag.Should().Be("adbe:AnnualizedRecurringRevenue");
    }

    [Fact]
    public void TryResolveConcept_UppercasePrefix_IsLoweredInStoredTag()
    {
        // Prefix casing follows the filer; the stored tag must not split one
        // concept across FinancialConcept rows by casing alone.
        XbrlFactExtractionService.TryResolveConcept(
            Fact("ADBE", "Subscribers", "http://www.adobe.com/20231201"),
            out _,
            out var tag
        );

        tag.Should().Be("adbe:Subscribers");
    }

    [Theory]
    [InlineData("country", "http://xbrl.sec.gov/country/2023")]
    [InlineData("ecd", "http://xbrl.sec.gov/ecd/2023")]
    [InlineData("us-types", "http://xbrl.us/us-types/2009-01-31")]
    [InlineData("ifrs", "https://xbrl.ifrs.org/taxonomy/2023-03-23/ifrs")]
    [InlineData("xbrli", "http://www.xbrl.org/2003/instance")]
    public void TryResolveConcept_StandardsBodyNamespace_IsRejected(string prefix, string ns)
    {
        XbrlFactExtractionService
            .TryResolveConcept(Fact(prefix, "Anything", ns), out _, out _)
            .Should()
            .BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a uri")]
    public void TryResolveConcept_MissingOrUnparseableNamespace_IsRejected(string ns)
    {
        XbrlFactExtractionService
            .TryResolveConcept(Fact("acme", "Metric", ns), out _, out _)
            .Should()
            .BeFalse();
    }

    [Fact]
    public void IsFilerExtensionNamespace_LookalikeDomainSuffix_IsNotAStandardsBody()
    {
        // "notsec.gov"-style hosts must not pass by substring accident; only an
        // exact registrable-domain match (or a true subdomain) is standard.
        XbrlFactExtractionService
            .IsFilerExtensionNamespace("http://mycompanyfasb.org/2024")
            .Should()
            .BeTrue();
    }
}
