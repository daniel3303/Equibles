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

    [Fact]
    public void IsInvestorRelationsPage_OgTypeArticle_ReturnsFalse_EvenWithIrTitleAndKeywords()
    {
        // The real failure case: /investor client-side redirects to a press release.
        // That article is full of IR boilerplate (an "Investor Relations" contact
        // block, "financial results") and even an IR-ish title, so it would clear the
        // keyword check — but og:type=article means it's a single news item, not the
        // IR portal, so it must be rejected.
        const string html =
            "<html><head><title>Acme to Participate in Investor Conferences</title>"
            + "<meta property=\"og:type\" content=\"article\" /></head><body>"
            + "<h1>Acme to Participate in Investor Conferences</h1>"
            + "<p>Investor Contact: Jane Roe, Vice President, Investor Relations.</p>"
            + "<p>The webcasts will discuss the most recently reported financial results.</p>"
            + "</body></html>";

        InvestorRelationsPageValidator.IsInvestorRelationsPage(html).Should().BeFalse();
    }

    [Fact]
    public void IsInvestorRelationsPage_ArticlePropertyMetadata_ReturnsFalse()
    {
        // Even without og:type, an article:* property marks the page as a single
        // news article rather than an IR landing page.
        const string html =
            "<html><head><title>Investor Relations</title>"
            + "<meta property=\"article:modified_time\" content=\"2019-05-20\" /></head>"
            + "<body><p>Quarterly results and SEC filings.</p></body></html>";

        InvestorRelationsPageValidator.IsInvestorRelationsPage(html).Should().BeFalse();
    }

    [Fact]
    public void IsInvestorRelationsPage_OgTypeArticleViaNameAttribute_ReturnsFalse()
    {
        // Some CMSs emit name="og:type" instead of the spec's property="og:type";
        // both forms must be honoured.
        const string html =
            "<html><head><title>Investor Relations | Acme</title>"
            + "<meta name=\"og:type\" content=\"article\" /></head>"
            + "<body><p>News.</p></body></html>";

        InvestorRelationsPageValidator.IsInvestorRelationsPage(html).Should().BeFalse();
    }

    [Fact]
    public void IsInvestorRelationsPage_SiteWideArticlePublisherMeta_DoesNotReject()
    {
        // article:publisher / article:author are injected site-wide by some CMSs
        // (WordPress/Yoast) on every page, including og:type=website IR hubs. They
        // must NOT trigger the article guard — only the per-article timestamps and
        // og:type=article do — so a genuine IR page carrying them still validates.
        const string html =
            "<html><head><title>Investor Relations | Acme</title>"
            + "<meta property=\"og:type\" content=\"website\" />"
            + "<meta property=\"article:publisher\" content=\"https://facebook.com/acme\" /></head>"
            + "<body><h1>Quarterly earnings and SEC filings</h1></body></html>";

        InvestorRelationsPageValidator.IsInvestorRelationsPage(html).Should().BeTrue();
    }

    [Fact]
    public void IsInvestorRelationsPage_OgTypeWebsite_WithIrKeywords_ReturnsTrue()
    {
        // The real IR portal declares og:type=website (not article) — it must still
        // pass on its IR content so the article guard doesn't reject genuine pages.
        const string html =
            "<html><head><title>Events &amp; Presentations</title>"
            + "<meta property=\"og:type\" content=\"website\" /></head><body>"
            + "<h1>Quarterly earnings webcasts and SEC filings</h1>"
            + "</body></html>";

        InvestorRelationsPageValidator.IsInvestorRelationsPage(html).Should().BeTrue();
    }
}
