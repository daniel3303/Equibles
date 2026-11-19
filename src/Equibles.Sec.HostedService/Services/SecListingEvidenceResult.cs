using Equibles.CommonStocks.Data.Models;

namespace Equibles.Sec.HostedService.Services;

public record SecListingEvidenceResult(
    SecListingEvidenceOutcome Outcome,
    string RegistrationAccessionNumber = null,
    string CertificationAccessionNumber = null,
    string ProspectusAccessionNumber = null,
    DateOnly? ProspectusFiledOn = null
);
