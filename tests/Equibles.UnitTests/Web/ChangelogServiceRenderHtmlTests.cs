using Equibles.Web.Services;

namespace Equibles.UnitTests.Web;

/// <summary>
/// RenderHtml's documented contract has two promises: it renders the bundled
/// CHANGELOG.md to HTML, and it does so with raw HTML disabled because "rendering
/// it with HTML enabled would be a latent stored-XSS sink". The second promise is
/// the security-critical one and is exercised by no existing test (the file has
/// zero coverage). A refactor that drops <c>.DisableHtml()</c> from the Markdig
/// pipeline would still render Markdown correctly and pass any happy-path test,
/// while silently turning the changelog page into a stored-XSS sink. This pins
/// the no-raw-HTML contract: a CHANGELOG containing a script tag must come back
/// escaped, never as a live element.
/// </summary>
public class ChangelogServiceRenderHtmlTests
{
    [Fact]
    public void RenderHtml_ChangelogContainsRawHtml_EscapesItInsteadOfEmittingLiveMarkup()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "CHANGELOG.md");
        var hadExisting = File.Exists(path);
        var backup = hadExisting ? File.ReadAllText(path) : null;

        try
        {
            File.WriteAllText(path, "# Changelog\n\n<script>alert(1)</script>\n");

            var html = new ChangelogService().RenderHtml();

            html.Should().NotBeNull();
            // Markdown is actually rendered (not returned raw). UseAdvancedExtensions
            // adds an auto heading id, so match the element shape, not an exact tag.
            html.Should().Contain("<h1").And.Contain("Changelog</h1>");
            // The security contract: raw HTML must not pass through as a live tag,
            // and must be present escaped (proving it was neutralised, not dropped).
            html.Should().NotContain("<script>");
            html.Should().Contain("&lt;script&gt;");
        }
        finally
        {
            if (backup != null)
            {
                File.WriteAllText(path, backup);
            }
            else if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
