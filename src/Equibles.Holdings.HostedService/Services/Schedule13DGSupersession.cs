using Equibles.Data;
using Equibles.Holdings.Data.Models;

namespace Equibles.Holdings.HostedService.Services;

/// <summary>
/// Which stored 13D/13G filings are later than a given one for the same position. Holdings keep
/// one row per filer, security, form and event date, so importing an earlier filing after a later
/// one would overwrite the later figures.
/// </summary>
public static class Schedule13DGSupersession
{
    /// <summary>
    /// Filings by <paramref name="filerCik"/> for <paramref name="issuerCusip"/> with the same form
    /// and event date, filed later (same-day ties go to the amendment, then the higher accession),
    /// whose position is stored in the holdings table. A later filing matches by its filed CUSIP or
    /// by the CUSIP its position was stored under, which differ when the subject company resolved it.
    /// </summary>
    public static IQueryable<BeneficialOwnershipFiling> LaterStoredFilings(
        EquiblesFinancialDbContext dbContext,
        string filerCik,
        string issuerCusip,
        FilingType filingType,
        DateOnly dateOfEvent,
        DateOnly filingDate,
        bool isAmendment,
        string accessionNumber
    )
    {
        var filer = filerCik.Trim().TrimStart('0');
        var cusip = issuerCusip.Trim().ToUpperInvariant();
        return dbContext
            .Set<BeneficialOwnershipFiling>()
            .Where(later =>
                later.FilerCik == filer
                && later.FilingType == filingType
                && later.DateOfEvent == dateOfEvent
                && (
                    later.IssuerCusip.ToUpper() == cusip
                    || dbContext
                        .Set<InstitutionalHolding>()
                        .Any(holding =>
                            holding.AccessionNumber == later.AccessionNumber
                            && holding.Cusip.ToUpper() == cusip
                        )
                )
                && later.AccessionNumber != accessionNumber
                && (
                    later.FilingDate > filingDate
                    || later.FilingDate == filingDate
                        && (
                            later.IsAmendment && !isAmendment
                            || later.IsAmendment == isAmendment
                                && string.Compare(later.AccessionNumber, accessionNumber) > 0
                        )
                )
                && dbContext
                    .Set<InstitutionalHolding>()
                    .Any(holding => holding.AccessionNumber == later.AccessionNumber)
            );
    }
}
