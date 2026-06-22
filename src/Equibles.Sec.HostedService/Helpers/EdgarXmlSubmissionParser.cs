using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Equibles.Core.Extensions;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Integrations.Sec.Models;

namespace Equibles.Sec.HostedService.Helpers;

/// <summary>
/// Shared parsing for SEC issuer-feed XML submissions (Form 144, Form D, N-CEN, NPORT-P).
/// Each of these forms wraps its payload in the same SGML envelope and uses the
/// <c>edgarSubmission</c> root, so envelope stripping, ampersand escaping, and the
/// parse/skip/error-report flow are identical across their processors.
/// </summary>
internal static class EdgarXmlSubmissionParser
{
    private const string XmlEnvelopeStart = "<XML>";
    private const string XmlEnvelopeEnd = "</XML>";

    // Optional fields the filer leaves blank are reported as the literal "N/A"; treat as absent.
    private const string NotApplicable = "N/A";

    /// <summary>
    /// Strips the SGML envelope and escapes stray ampersands so the payload parses as XML.
    /// </summary>
    internal static string SanitizeXml(string xml)
    {
        // SEC filings wrap the actual XML inside an SGML envelope (one .txt holds the whole
        // submission); pull out the <XML>...</XML> body before parsing.
        var xmlStart = xml.IndexOf(XmlEnvelopeStart, StringComparison.OrdinalIgnoreCase);
        var xmlEnd = xml.IndexOf(XmlEnvelopeEnd, StringComparison.OrdinalIgnoreCase);
        if (xmlStart >= 0 && xmlEnd > xmlStart)
        {
            xml = xml[(xmlStart + XmlEnvelopeStart.Length)..xmlEnd].Trim();
        }

        // Escape stray ampersands that aren't already part of an entity.
        return Regex.Replace(xml, @"&(?!(amp|lt|gt|quot|apos|#\d+|#x[\da-fA-F]+);)", "&amp;");
    }

    /// <summary>
    /// Sanitizes the raw submission and returns its <c>edgarSubmission</c> root, or
    /// <c>null</c> when the filing isn't XML or is malformed (logging and reporting the case).
    /// </summary>
    /// <param name="filingTypeName">Human-readable form label used in log messages (e.g. "Form 144").</param>
    /// <param name="errorContext">Error-reporter context label (e.g. "Form144.ParseXml").</param>
    internal static async Task<XElement> TryParseSubmission(
        string content,
        FilingData filing,
        string companyTicker,
        string filingTypeName,
        string errorContext,
        ILogger logger,
        ErrorReporter errorReporter
    )
    {
        var sanitized = SanitizeXml(content);

        // These forms are XML-only, so a submission without the <edgarSubmission> root is
        // unexpected — skip quietly.
        if (!sanitized.Contains("<edgarSubmission", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogDebug(
                "Skipping non-XML {FilingType} filing for {Ticker} - {AccessionNumber}",
                filingTypeName,
                companyTicker,
                filing.AccessionNumber
            );
            return null;
        }

        try
        {
            return XDocument.Parse(sanitized).Root;
        }
        catch (System.Xml.XmlException ex)
        {
            logger.LogWarning(
                ex,
                "Malformed {FilingType} XML for {Ticker} - {AccessionNumber}",
                filingTypeName,
                companyTicker,
                filing.AccessionNumber
            );
            await errorReporter.Report(
                ErrorSource.DocumentScraper,
                errorContext,
                ex.Message,
                ex.StackTrace,
                $"ticker: {companyTicker}, accession: {filing.AccessionNumber}"
            );
            return null;
        }
    }

    /// <summary>
    /// First child element with the given local name (namespace-agnostic), or <c>null</c>.
    /// EDGAR forms declare assorted default namespaces, so matching on the local name keeps
    /// the navigation prefix-independent.
    /// </summary>
    internal static XElement El(XElement parent, string name) =>
        parent?.Elements().FirstOrDefault(e => e.Name.LocalName == name);

    /// <summary>
    /// All child elements with the given local name (namespace-agnostic), never <c>null</c>.
    /// </summary>
    internal static IEnumerable<XElement> Els(XElement parent, string name) =>
        parent?.Elements().Where(e => e.Name.LocalName == name) ?? [];

    /// <summary>
    /// Trimmed text of the named child element, or <c>null</c> when missing or empty.
    /// </summary>
    internal static string Val(XElement parent, string name)
    {
        var value = El(parent, name)?.Value?.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    /// <summary>
    /// Trimmed value of the named attribute (namespace-agnostic), or <c>null</c> when missing or empty.
    /// </summary>
    internal static string Attr(XElement element, string name)
    {
        var value = element
            ?.Attributes()
            .FirstOrDefault(a => a.Name.LocalName == name)
            ?.Value?.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    /// <summary>
    /// Caps a parsed value at its destination column's length so an oversized free-text field
    /// (e.g. a foreign issuer's long ADR class-title description) can't fail the whole filing's
    /// INSERT with a length-overflow. Pass the column's <c>[MaxLength]</c> as
    /// <paramref name="maxLength"/>; <c>null</c> and shorter values pass through unchanged.
    /// </summary>
    internal static string Truncate(string value, int maxLength) => value.TruncateToFit(maxLength);

    /// <summary>
    /// Trimmed text with the "N/A" placeholder and blanks normalized to <c>null</c>.
    /// </summary>
    internal static string Clean(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var trimmed = value.Trim();
        return trimmed.Equals(NotApplicable, StringComparison.OrdinalIgnoreCase) ? null : trimmed;
    }

    /// <summary>
    /// Parses a date against the supplied exact formats in invariant culture, or <c>null</c> when
    /// blank or unparseable. Callers pass their form's accepted <paramref name="formats"/>, which
    /// differ per submission type.
    /// </summary>
    internal static DateOnly? ParseDate(string value, string[] formats)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        return DateOnly.TryParseExact(
            value.Trim(),
            formats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsed
        )
            ? parsed
            : null;
    }
}
