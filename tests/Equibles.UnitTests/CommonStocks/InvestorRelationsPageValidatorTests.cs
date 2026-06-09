using Equibles.CommonStocks.HostedService.Services;

namespace Equibles.UnitTests.CommonStocks;

public class InvestorRelationsPageValidatorTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsInvestorRelationsPage_EmptyHtml_ReturnsFalse(string html)
    {
        InvestorRelationsPageValidator.IsInvestorRelationsPage(html).Should().BeFalse();
    }

    [Fact]
    public void IsInvestorRelationsPage_TitleCarriesIrPhrase_ReturnsTrue()
    {
        // Contract: an IR page almost always says so in its <title>; that alone is
        // a strong enough signal.
        const string html =
            "<html><head><title>Investor Relations | Acme Corp</title></head>"
            + "<body><p>Welcome.</p></body></html>";

        InvestorRelationsPageValidator.IsInvestorRelationsPage(html).Should().BeTrue();
    }

    [Fact]
    public void IsInvestorRelationsPage_TwoDistinctBodyKeywords_ReturnsTrue()
    {
        // No IR phrase in the title, but the content is unmistakably an IR page.
        const string html =
            "<html><head><title>Acme Corporation</title></head><body>"
            + "<h1>Latest SEC filings and quarterly results</h1>"
            + "</body></html>";

        InvestorRelationsPageValidator.IsInvestorRelationsPage(html).Should().BeTrue();
    }

    [Fact]
    public void IsInvestorRelationsPage_SingleFooterMention_ReturnsFalse()
    {
        // A homepage that merely links to "Investor Relations" in its nav is not an
        // IR page: one keyword hit, no IR title phrase.
        const string html =
            "<html><head><title>Acme Corporation - Home</title></head><body>"
            + "<nav><a href=\"/ir\">Investor Relations</a></nav>"
            + "<p>We make great products.</p></body></html>";

        InvestorRelationsPageValidator.IsInvestorRelationsPage(html).Should().BeFalse();
    }

    [Fact]
    public void IsInvestorRelationsPage_KeywordsOnlyInScript_ReturnsFalse()
    {
        // Script/style content must not count as visible page text — guards against
        // analytics blobs that happen to mention IR terms.
        const string html =
            "<html><head><title>Home</title></head><body>"
            + "<script>var x = 'investor relations sec filings quarterly results';</script>"
            + "<p>Welcome to Acme.</p></body></html>";

        InvestorRelationsPageValidator.IsInvestorRelationsPage(html).Should().BeFalse();
    }
}
