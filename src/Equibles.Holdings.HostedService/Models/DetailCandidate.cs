using Equibles.Holdings.Data.Models;

namespace Equibles.Holdings.HostedService.Models;

/// <summary>A stored 13D/13G filing whose cover-page details are not on file yet.</summary>
public record DetailCandidate(
    string AccessionNumber,
    FilingType FilingType,
    DateOnly FilingDate,
    string Cik
);
