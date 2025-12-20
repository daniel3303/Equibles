using System.Text.RegularExpressions;
using Equibles.Core.AutoWiring;
using Microsoft.Extensions.DependencyInjection;
using ReverseMarkdown;

namespace Equibles.Sec.BusinessLogic;

[Service(ServiceLifetime.Scoped, typeof(ISecDocumentHtmlToMarkdownConverter))]
public class SecDocumentHtmlToMarkdownConverter : ISecDocumentHtmlToMarkdownConverter {
    public SecDocumentHtmlToMarkdownConverter() {
    }

    public string Convert(string html) {
        if (string.IsNullOrWhiteSpace(html)) {
            return string.Empty;
        }

        // Strip style attributes to prevent ReverseMarkdown's ParseStyle
        // from throwing on duplicate CSS properties in SEC filings
        html = Regex.Replace(html, @"\s*style\s*=\s*""[^""]*""", "");

        var markdownConverter = CreateMarkdownConverter();
        var markdown = markdownConverter.Convert(html);

        // Ensure blank line before pipe tables (Markdig requires it)
        markdown = Regex.Replace(markdown, @"([^\n|])\n(\|)", "$1\n\n$2");

        // Ensure blank line after pipe tables (Markdig requires it)
        markdown = Regex.Replace(markdown, @"(\|)\n([^\|\n])", "$1\n\n$2");

        return markdown;
    }

    private Converter CreateMarkdownConverter() {
        var config = new Config {
            GithubFlavored = true,
            RemoveComments = true,
            SmartHrefHandling = true,
            UnknownTags = Config.UnknownTagsOption.Bypass,
        };

        return new Converter(config);
    }
}