using Equibles.CommonStocks.Data.Models;
using Equibles.Integrations.Sec.Models;

namespace Equibles.Sec.HostedService.Services;

/// <summary>
/// Reads a registrant's EDGAR filing index for proof that one of its securities was just listed on
/// an exchange: the registrant's 8-A12B, then the exchange's certification (CERT), then a final
/// prospectus (424B4) filed on or after the certification. SEC's ticker directory can carry such a
/// registrant with a blank exchange for days after trading starts; certification alone is not
/// enough, because registrants still amending, withdrawn or long delisted hold one too.
/// </summary>
public static class SecListingEvidence
{
    /// <summary>
    /// How long after its final prospectus a registrant is admitted on this evidence. By then SEC's
    /// directory states the exchange itself, and an older prospectus may belong to a listing that
    /// has since ended.
    /// </summary>
    public const int AdmissionWindowDays = 30;

    /// <summary>
    /// How far back the filing index is read. A certification older than this can only lead to a
    /// prospectus outside the admission window, so missing it changes no outcome.
    /// </summary>
    public const int LookbackDays = 180;

    private const string RegistrationForm = "8-A12B";
    private const string CertificationFormPrefix = "CERT";
    private const string FinalProspectusForm = "424B4";

    // A delisting (Form 25, or 25-NSE filed by the exchange), a deregistration under Section 12, or
    // a registration withdrawal after the certification means the listing did not stand.
    private static readonly string[] EndingForms = ["25", "25-NSE", "RW", "15-12B", "15-12G"];

    public static SecListingEvidenceResult Evaluate(IEnumerable<FilingData> filings, DateOnly today)
    {
        var dated = filings
            .Where(f => f.Form != null && f.FilingDate != DateOnly.MinValue)
            .ToList();

        var certification = Latest(
            dated.Where(f => f.Form.StartsWith(CertificationFormPrefix, StringComparison.Ordinal))
        );
        var registration =
            certification == null
                ? null
                : Latest(
                    dated.Where(f =>
                        IsForm(f, RegistrationForm) && f.FilingDate <= certification.FilingDate
                    )
                );
        if (certification == null || registration == null)
            return new SecListingEvidenceResult(SecListingEvidenceOutcome.NoCertification);

        var ending = dated.Any(f =>
            EndingForms.Any(form => IsForm(f, form)) && f.FilingDate >= certification.FilingDate
        );
        var prospectus = dated
            .Where(f => IsForm(f, FinalProspectusForm) && f.FilingDate >= certification.FilingDate)
            .OrderBy(f => f.FilingDate)
            .ThenBy(f => f.AccessionNumber, StringComparer.Ordinal)
            .FirstOrDefault();

        var outcome =
            ending ? SecListingEvidenceOutcome.Withdrawn
            : prospectus == null ? SecListingEvidenceOutcome.NoFinalProspectus
            : today.DayNumber - prospectus.FilingDate.DayNumber > AdmissionWindowDays
                ? SecListingEvidenceOutcome.Stale
            : SecListingEvidenceOutcome.Admitted;
        return new SecListingEvidenceResult(
            outcome,
            registration.AccessionNumber,
            certification.AccessionNumber,
            prospectus?.AccessionNumber,
            prospectus?.FilingDate
        );
    }

    /// <summary>
    /// Whether a stored admission still holds on <paramref name="today"/>: the window is counted from
    /// the prospectus date, so an admission read earlier lapses without another EDGAR read.
    /// </summary>
    public static bool StillAdmitted(SecListingEvidenceCheck check, DateOnly today) =>
        check.Outcome == SecListingEvidenceOutcome.Admitted
        && check.ProspectusFiledOn is { } filedOn
        && today.DayNumber - filedOn.DayNumber <= AdmissionWindowDays;

    // An amendment ("8-A12B/A") restates the same form.
    private static bool IsForm(FilingData filing, string form) =>
        filing.Form == form || filing.Form == form + "/A";

    private static FilingData Latest(IEnumerable<FilingData> filings) =>
        filings
            .OrderByDescending(f => f.FilingDate)
            .ThenByDescending(f => f.AccessionNumber, StringComparer.Ordinal)
            .FirstOrDefault();
}
