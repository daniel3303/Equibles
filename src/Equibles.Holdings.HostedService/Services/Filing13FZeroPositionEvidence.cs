using Equibles.Holdings.HostedService.Models;

namespace Equibles.Holdings.HostedService.Services;

internal static class Filing13FZeroPositionEvidence
{
    internal static bool IsOriginal(Parsed13FFiling filing) =>
        !filing.IsAmendment && HasVerifiedZeroQuantities(filing);

    internal static bool IsRestatement(Parsed13FFiling filing) =>
        filing.IsAmendment
        && string.Equals(filing.AmendmentType, "RESTATEMENT", StringComparison.OrdinalIgnoreCase)
        && HasVerifiedZeroQuantities(filing);

    private static bool HasVerifiedZeroQuantities(Parsed13FFiling filing) =>
        filing.CompleteSubmissionVerified
        && !filing.ConfidentialTreatmentRequested
        && !filing.ConfidentialOmitted
        && filing.ReportType == "13F HOLDINGS REPORT"
        && filing.OtherIncludedManagersCount == 0
        && filing.OtherManagers.Count == 0
        && filing.CoverPageOtherManagers.Count == 0
        && filing.Holdings.Count > 0
        && filing.TableEntryTotal == filing.Holdings.Count
        && filing.TableValueTotal == 0
        && filing.Holdings.All(h =>
            h.HasExplicitZeroQuantities && string.IsNullOrWhiteSpace(h.OtherManagers)
        );
}
