using System.Xml.Linq;
using Equibles.Holdings.HostedService.Models;
using Equibles.Integrations.Sec.Models;
using Equibles.Sec.BusinessLogic;

namespace Equibles.Holdings.HostedService.Services;

internal static class Filing13FSubmissionParser
{
    // A partial envelope must never turn a restatement into a holdings-removing amendment.
    internal static Parsed13FSubmission Parse(
        string submission,
        EdgarDailyIndexEntry entry,
        Filing13FXmlParser parser
    )
    {
        if (
            string.IsNullOrWhiteSpace(submission)
            || !submission.TrimEnd().EndsWith("</SEC-DOCUMENT>", StringComparison.OrdinalIgnoreCase)
        )
            return null;

        var artifacts = SecDocumentEnvelopeParser.EnumerateArtifacts(submission);
        if (
            artifacts.Count != CountTag(submission, "<DOCUMENT>")
            || artifacts.Count != CountTag(submission, "</DOCUMENT>")
        )
            return null;
        var covers = artifacts.Where(a => a.Type is "13F-HR" or "13F-HR/A").ToList();
        if (covers.Count != 1 || covers[0].Type != entry.FormType)
            return null;
        var cover = XmlBody(covers[0].Body);
        if (cover == null)
            return null;
        // The XML-only parser permits an index fallback; this preferred route must prove its filer.
        var declaredCiks =
            XDocument
                .Parse(cover)
                .Root?.Elements()
                .Where(element => element.Name.LocalName == "headerData")
                .SelectMany(header =>
                    header.Descendants().Where(element => element.Name.LocalName == "cik")
                )
                .Select(element => element.Value.Trim().TrimStart('0'))
                .ToList()
            ?? [];
        if (declaredCiks.Count != 1 || declaredCiks[0] != entry.Cik.TrimStart('0'))
            return null;

        var filing = parser.ParseCoverPage(
            cover,
            entry.AccessionNumber,
            entry.Cik,
            entry.DateFiled
        );
        if (
            filing.Cik != entry.Cik.TrimStart('0')
            || filing.IsAmendment != (entry.FormType == "13F-HR/A")
            || filing.PeriodOfReport == DateOnly.MinValue
        )
            return null;

        foreach (var artifact in artifacts.Where(a => a.Type == "INFORMATION TABLE"))
        {
            var xml = XmlBody(artifact.Body);
            if (xml == null)
                return null;
            filing.Holdings.AddRange(parser.ParseInformationTable(xml));
        }

        // A filer's cover totals can disagree with its own table. Preserve the safe SEC
        // filenames so the existing standalone-XML route need not rediscover this directory.
        var fallback = new Parsed13FSubmission(null, artifacts.Select(a => a.FileName).ToList());
        if (
            filing.Holdings.Count != filing.TableEntryTotal
            || filing.Holdings.Sum(h => h.Value) != filing.TableValueTotal
            || (filing.Holdings.Count == 0 && !filing.IsAmendment)
        )
            return fallback;
        filing.CompleteSubmissionVerified = true;
        return fallback with { Filing = filing };
    }

    private static int CountTag(string value, string tag)
    {
        var count = 0;
        var offset = 0;
        while ((offset = value.IndexOf(tag, offset, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            count++;
            offset += tag.Length;
        }
        return count;
    }

    private static string XmlBody(string body)
    {
        body = body?.Trim();
        if (
            body == null
            || !body.StartsWith("<XML>", StringComparison.OrdinalIgnoreCase)
            || !body.EndsWith("</XML>", StringComparison.OrdinalIgnoreCase)
        )
            return null;
        return body[5..^6].Trim();
    }
}

internal sealed record Parsed13FSubmission(Parsed13FFiling Filing, List<string> ArtifactNames);
