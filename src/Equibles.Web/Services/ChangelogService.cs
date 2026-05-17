using Equibles.Core.AutoWiring;
using Markdig;

namespace Equibles.Web.Services;

/// <summary>
/// Reads the bundled CHANGELOG.md and renders it to HTML for the in-app
/// changelog page. Keeps file I/O and Markdown rendering out of the controller.
/// </summary>
[Service]
public class ChangelogService
{
    // Raw HTML is disabled: the CHANGELOG is a trusted repo file, but rendering
    // it with HTML enabled would be a latent stored-XSS sink.
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .DisableHtml()
        .Build();

    /// <summary>
    /// Returns the changelog rendered as HTML, or <c>null</c> when the file
    /// was not shipped with the build (caller falls back to the GitHub copy).
    /// </summary>
    public string RenderHtml()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "CHANGELOG.md");
        if (!File.Exists(path))
        {
            return null;
        }

        var markdown = File.ReadAllText(path);
        return Markdown.ToHtml(markdown, Pipeline);
    }
}
