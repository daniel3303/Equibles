using Equibles.Holdings.Data.Models;

namespace Equibles.Holdings.HostedService.Models;

/// <summary>A stored 13D/13G with the type codes of each reporting person.</summary>
public record BeneficialOwnershipEvidenceRow(
    string AccessionNumber,
    FilingType FilingType,
    DateOnly FilingDate,
    string FilerCik,
    ReportingPersonTypes FilingPersonTypes,
    IReadOnlyList<(string Cik, ReportingPersonTypes Types)> Persons
)
{
    /// <summary>The Rule 13d-1 paragraphs a 13G is filed under.</summary>
    public Schedule13GRules Rules { get; init; }

    /// <summary>Item 3 codes outside SEC's list, as filed; null when none.</summary>
    public string UnrecognizedFilingPersonTypeCodes { get; init; }
}
