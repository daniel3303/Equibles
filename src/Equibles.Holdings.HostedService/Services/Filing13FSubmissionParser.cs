using Equibles.Holdings.HostedService.Models;
using Equibles.Integrations.Sec.Models;
using Equibles.Sec.BusinessLogic;

namespace Equibles.Holdings.HostedService.Services;

internal static class Filing13FSubmissionParser
{
    // A partial envelope must never turn a restatement into a holdings-removing amendment.
    internal static Parsed13FFiling Parse(
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
        var covers = artifacts.Where(a => a.Type is "13F-HR" or "13F-HR/A").ToList();
        if (covers.Count != 1 || covers[0].Type != entry.FormType)
            return null;
        var cover = XmlBody(covers[0].Body);
        if (cover == null)
            return null;

        var filing = parser.ParseCoverPage(cover, entry.AccessionNumber, entry.Cik, entry.DateFiled);
        if (
            filing.Cik != entry.Cik.TrimStart('0')
            || filing.IsAmendment != (entry.FormType == "13F-HR/A")
            || filing.PeriodOfReport == DateOnly.MinValue
            || filing.TableEntryTotal == null
            || filing.TableValueTotal == null
        )
            return null;

        foreach (var artifact in artifacts.Where(a => a.Type == "INFORMATION TABLE"))
        {
            var xml = XmlBody(artifact.Body);
            if (xml == null)
                return null;
            filing.Holdings.AddRange(parser.ParseInformationTable(xml));
        }

        if (
            filing.Holdings.Count != filing.TableEntryTotal
            || filing.Holdings.Sum(h => h.Value) != filing.TableValueTotal
            || (filing.Holdings.Count == 0 && !filing.IsAmendment)
        )
            return null;
        return filing;
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
