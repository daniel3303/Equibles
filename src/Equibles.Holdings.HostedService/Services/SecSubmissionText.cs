using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Equibles.Holdings.HostedService.Models;
using HtmlAgilityPack;

namespace Equibles.Holdings.HostedService.Services;

/// <summary>
/// Reads an EDGAR complete submission (<c>{accession}.txt</c>): its SGML header, one embedded
/// document, and that document as plain text whether it was filed as text or HTML.
/// </summary>
public static partial class SecSubmissionText
{
    private static readonly HashSet<string> BlockElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "address",
        "article",
        "blockquote",
        "br",
        "caption",
        "center",
        "dd",
        "div",
        "dl",
        "dt",
        "footer",
        "h1",
        "h2",
        "h3",
        "h4",
        "h5",
        "h6",
        "header",
        "hr",
        "li",
        "ol",
        "p",
        "pre",
        "section",
        "table",
        "tbody",
        "td",
        "tfoot",
        "th",
        "thead",
        "tr",
        "ul",
    };

    private static readonly HashSet<string> SkippedElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "head",
        "script",
        "style",
        "title",
    };

    /// <summary>The header facts, or null when the submission has no SGML header.</summary>
    public static SecSubmissionHeader ReadHeader(string submission)
    {
        if (string.IsNullOrEmpty(submission))
            return null;

        var start = submission.IndexOf("<SEC-HEADER>", StringComparison.OrdinalIgnoreCase);
        if (start < 0)
            return null;
        var end = submission.IndexOf("</SEC-HEADER>", start, StringComparison.OrdinalIgnoreCase);
        var headerText = end < 0 ? submission[start..] : submission[start..end];

        var header = new SecSubmissionHeader();
        List<SecSubmissionParty> section = null;
        string sectionCik = null;
        string sectionName = null;

        void CloseSection()
        {
            section?.Add(new SecSubmissionParty(sectionCik, sectionName));
            section = null;
            sectionCik = null;
            sectionName = null;
        }

        foreach (var rawLine in headerText.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.Length == 0)
                continue;
            var indented = char.IsWhiteSpace(line[0]);
            var (key, value) = SplitHeaderLine(line);
            if (key == null)
                continue;

            if (!indented)
            {
                switch (key)
                {
                    case "ACCESSION NUMBER":
                        header.AccessionNumber = value;
                        continue;
                    case "CONFORMED SUBMISSION TYPE":
                        header.SubmissionType = value;
                        continue;
                    case "SUBJECT COMPANY":
                        CloseSection();
                        section = header.SubjectCompanies;
                        continue;
                    case "FILED BY":
                    case "FILER":
                        CloseSection();
                        section = header.FiledBy;
                        continue;
                    default:
                        // Any other top-level key (a group member, a series list) ends the block.
                        CloseSection();
                        continue;
                }
            }

            if (section == null)
                continue;
            // The first CIK and name of a block are the company's own; later ones never repeat.
            if (key == "CENTRAL INDEX KEY" && sectionCik == null)
                sectionCik = value?.TrimStart('0');
            else if (key == "COMPANY CONFORMED NAME" && sectionName == null)
                sectionName = value;
        }

        CloseSection();
        return header;
    }

    /// <summary>
    /// The body of the first document whose type is <paramref name="documentType"/>, or null. A
    /// combined submission can lead with another form's document.
    /// </summary>
    public static string ReadDocument(string submission, string documentType)
    {
        if (string.IsNullOrEmpty(submission) || string.IsNullOrWhiteSpace(documentType))
            return null;

        foreach (Match document in DocumentPattern().Matches(submission))
        {
            var body = document.Groups["body"].Value;
            var type = DocumentTypePattern().Match(body);
            if (
                !type.Success
                || !string.Equals(
                    type.Groups["type"].Value.Trim(),
                    documentType.Trim(),
                    StringComparison.OrdinalIgnoreCase
                )
            )
                continue;

            var text = TextPattern().Match(body);
            return text.Success ? text.Groups["text"].Value : null;
        }

        return null;
    }

    /// <summary>
    /// The document's visible text, one line per block element or table cell, with entities
    /// decoded and non-breaking spaces turned into spaces.
    /// </summary>
    public static string ToPlainText(string document)
    {
        if (string.IsNullOrEmpty(document))
            return string.Empty;

        string text;
        if (HtmlMarker().IsMatch(document))
        {
            var html = new HtmlDocument();
            html.LoadHtml(document);
            var builder = new StringBuilder(document.Length / 2);
            AppendText(html.DocumentNode, builder);
            text = builder.ToString();
        }
        else
        {
            text = PageMarker().Replace(document, "\n");
        }

        return text.Replace(' ', ' ').Replace(' ', ' ').Replace(' ', ' ');
    }

    private static void AppendText(HtmlNode node, StringBuilder builder)
    {
        switch (node.NodeType)
        {
            case HtmlNodeType.Text:
                builder.Append(WebUtility.HtmlDecode(((HtmlTextNode)node).Text));
                return;
            case HtmlNodeType.Comment:
                return;
            case HtmlNodeType.Element when SkippedElements.Contains(node.Name):
                return;
        }

        var block = node.NodeType == HtmlNodeType.Element && BlockElements.Contains(node.Name);
        // A superscript footnote marker glued to a figure ("280,000<sup>1</sup>") must stay apart.
        var separated =
            node.NodeType == HtmlNodeType.Element && node.Name is "sup" or "SUP" or "sub" or "SUB";
        var boundary = block ? '\n' : ' ';
        if (block || separated)
            builder.Append(boundary);
        foreach (var child in node.ChildNodes)
            AppendText(child, builder);
        if (block || separated)
            builder.Append(boundary);
    }

    private static (string Key, string Value) SplitHeaderLine(string line)
    {
        var colon = line.IndexOf(':');
        if (colon <= 0)
            return (null, null);
        var key = line[..colon].Trim().ToUpperInvariant();
        var value = line[(colon + 1)..].Trim();
        return (key, value.Length == 0 ? null : value);
    }

    [GeneratedRegex(
        @"<DOCUMENT>(?<body>.*?)</DOCUMENT>",
        RegexOptions.Singleline | RegexOptions.IgnoreCase
    )]
    private static partial Regex DocumentPattern();

    [GeneratedRegex(@"<TYPE>(?<type>[^\r\n<]*)", RegexOptions.IgnoreCase)]
    private static partial Regex DocumentTypePattern();

    [GeneratedRegex(
        @"<TEXT>(?<text>.*?)(?:</TEXT>|$)",
        RegexOptions.Singleline | RegexOptions.IgnoreCase
    )]
    private static partial Regex TextPattern();

    [GeneratedRegex(@"<(?:html|body|table|div|p|br|font|span|td)\b", RegexOptions.IgnoreCase)]
    private static partial Regex HtmlMarker();

    [GeneratedRegex(@"</?PAGE>", RegexOptions.IgnoreCase)]
    private static partial Regex PageMarker();
}
