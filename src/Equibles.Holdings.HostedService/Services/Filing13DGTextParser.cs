using System.Text.RegularExpressions;
using Equibles.Core.AutoWiring;
using Equibles.Holdings.HostedService.Extensions;
using Equibles.Holdings.HostedService.Models;

namespace Equibles.Holdings.HostedService.Services;

/// <summary>
/// Reads a Schedule 13D or 13G filed before structured XML (forms <c>SC 13D</c>, <c>SC 13G</c> and
/// their amendments) from its EDGAR complete submission. The SGML header names the subject company
/// and the filer by CIK; the schedule, filed as text or HTML, follows the regulatory cover-page
/// template, whose captions this reads. A filing whose cover pages cannot all be read is refused,
/// never completed from partial answers.
/// </summary>
[Service]
public partial class Filing13DGTextParser
{
    /// <summary>
    /// Bump when a reading fix must reach stored filings: refused filings parked under an older
    /// version are read again by the next coverage walk. v1 is the first text reader.
    /// </summary>
    public const int Version = 1;

    /// <summary>
    /// Parses <paramref name="submission"/>, the complete submission text of
    /// <paramref name="accessionNumber"/>, filed on <paramref name="filingDate"/> as
    /// <paramref name="indexFormType"/>, the daily index's form, which types a refusal whose header
    /// cannot be read.
    /// </summary>
    public Schedule13DGTextParse Parse(
        string submission,
        string accessionNumber,
        DateOnly filingDate,
        string indexFormType
    )
    {
        var filing = new Parsed13DGFiling
        {
            AccessionNumber = accessionNumber,
            FilingDate = filingDate,
            SubmissionType = indexFormType,
            FilingType =
                indexFormType.ToHoldingsFilingType() is Data.Models.FilingType.Schedule13G
                    ? Data.Models.FilingType.Schedule13G
                    : Data.Models.FilingType.Schedule13D,
            IsAmendment = indexFormType.IsAmendmentFormType(),
            TextParserVersion = Version,
        };

        var header = SecSubmissionText.ReadHeader(submission);
        if (header == null)
            return Refuse(filing, "no submission header");

        var filingType = header.SubmissionType.ToHoldingsFilingType();
        if (
            filingType
            is not (Data.Models.FilingType.Schedule13D or Data.Models.FilingType.Schedule13G)
        )
            return Refuse(filing, "submission is not a Schedule 13D or 13G");
        filing.SubmissionType = header.SubmissionType;
        filing.FilingType = filingType.Value;
        filing.IsAmendment = header.SubmissionType.IsAmendmentFormType();

        if (
            header.AccessionNumber != null
            && !string.Equals(header.AccessionNumber, accessionNumber, StringComparison.Ordinal)
        )
            return Refuse(filing, "header names another accession");
        // The header is the only CIK source: one subject company and one filer, or no attribution.
        if (header.SubjectCompanies.Count != 1)
            return Refuse(filing, $"{header.SubjectCompanies.Count} subject companies");
        if (header.FiledBy.Count != 1)
            return Refuse(filing, $"{header.FiledBy.Count} filers");
        filing.IssuerCik = header.SubjectCompanies[0].Cik;
        filing.IssuerName = header.SubjectCompanies[0].Name;
        filing.FilerCik = header.FiledBy[0].Cik;
        if (string.IsNullOrEmpty(filing.FilerCik))
            return Refuse(filing, "filer without CIK");

        var document = SecSubmissionText.ReadDocument(submission, header.SubmissionType);
        if (document == null)
            return Refuse(filing, "no schedule document");
        var flat = Whitespace().Replace(SecSubmissionText.ToPlainText(document), " ");

        var (persons, coverEnd, failure) = Schedule13DGCoverPageReader.Read(flat);
        if (failure != null)
            return Refuse(
                filing,
                failure.CoverPage == 0
                    ? failure.Reason
                    : $"cover page {failure.CoverPage}: {failure.Reason}"
            );

        var cusips = Schedule13DGFrontMatterReader.ReadCusips(flat, coverEnd);
        var valid = cusips.Where(Schedule13DGTextValues.IsValidCusip).ToList();
        if (valid.Count > 1)
            return Refuse(filing, "several CUSIPs");
        // A malformed CUSIP is kept as filed; it resolves nothing, so the subject company decides.
        filing.IssuerCusip = valid.Count == 1 ? valid[0] : cusips.FirstOrDefault();

        var (eventDate, dateRefusal) = Schedule13DGFrontMatterReader.ReadEventDate(flat, coverEnd);
        if (dateRefusal != null)
            return Refuse(filing, dateRefusal);
        if (eventDate > filingDate)
            return Refuse(filing, "event date after filing date");
        filing.DateOfEvent = eventDate!.Value;
        filing.SecuritiesClassTitle = Schedule13DGFrontMatterReader.ReadClassTitle(flat, coverEnd);
        if (filing.FilingType == Data.Models.FilingType.Schedule13G)
            filing.RuleDesignations = Schedule13DGFrontMatterReader.ReadRules(flat, coverEnd);

        // Cover pages carry no CIK. Only a lone reporting person is, structurally, the filer.
        if (persons.Count == 1)
            persons[0].Cik = filing.FilerCik;
        filing.ReportingPersons = persons;
        return new Schedule13DGTextParse(filing, null);
    }

    private static Schedule13DGTextParse Refuse(Parsed13DGFiling filing, string reason)
    {
        filing.ReportingPersons = [];
        filing.RuleDesignations = [];
        return new Schedule13DGTextParse(filing, reason);
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
