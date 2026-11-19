using Equibles.CommonStocks.Data.Helpers;
using Equibles.CommonStocks.Data.Models;
using Equibles.Integrations.Sec.Contracts;
using Equibles.Integrations.Sec.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Sec.HostedService.Services;

/// <summary>
/// Admits a registrant SEC's ticker directory lists only with a blank exchange when its EDGAR
/// filings prove a fresh exchange listing (<see cref="SecListingEvidence"/>), so a newly listed
/// company enters the company sync before SEC fills in the exchange. Each read is stored as a
/// <see cref="SecListingEvidenceCheck"/>, the admission's audit stamp and the pace of EDGAR reads.
/// </summary>
public static class SecListingAdmission
{
    // Shorter than the hourly company sync, so every sync re-reads a registrant's filings and a
    // final prospectus is acted on within one sync of its filing.
    public static readonly TimeSpan RecheckInterval = TimeSpan.FromMinutes(50);

    public static async Task<List<CompanyInfo>> Admit(
        DbContext dbContext,
        ISecEdgarClient secEdgarClient,
        IReadOnlyCollection<CompanyInfo> unlisted,
        DateTime nowUtc,
        ILogger logger,
        CancellationToken cancellationToken = default
    )
    {
        var today = DateOnly.FromDateTime(nowUtc);
        var byCik = new Dictionary<string, CompanyInfo>(StringComparer.Ordinal);
        foreach (var company in unlisted)
        {
            if (CikNormalizer.Canonicalize(company.Cik) is { } cik)
                byCik.TryAdd(cik, company);
        }
        if (byCik.Count == 0)
            return [];

        var ciks = byCik.Keys.ToList();
        var stored = await dbContext
            .Set<SecListingEvidenceCheck>()
            .Where(check => ciks.Contains(check.Cik))
            .ToDictionaryAsync(check => check.Cik, StringComparer.Ordinal, cancellationToken);

        var admitted = new List<CompanyInfo>();
        foreach (var (cik, company) in byCik)
        {
            stored.TryGetValue(cik, out var check);
            if (check == null || nowUtc - check.CheckedAt >= RecheckInterval)
            {
                var result = await Read(secEdgarClient, company, today, logger, cancellationToken);
                if (result != null)
                {
                    check = Record(dbContext, check, cik, company, result, nowUtc, logger);
                    await dbContext.SaveChangesAsync(cancellationToken);
                }
            }
            if (check != null && SecListingEvidence.StillAdmitted(check, today))
                admitted.Add(company);
        }

        return admitted;
    }

    // An unreadable index, an HTTP timeout included, leaves the stored outcome in force and the
    // other registrants unaffected; the next sync reads it again. Only the caller's cancellation stops.
    private static async Task<SecListingEvidenceResult> Read(
        ISecEdgarClient secEdgarClient,
        CompanyInfo company,
        DateOnly today,
        ILogger logger,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var filings = await secEdgarClient.GetCompanyFilings(
                company.Cik,
                fromDate: today.AddDays(-SecListingEvidence.LookbackDays)
            );
            return SecListingEvidence.Evaluate(filings, today);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(
                ex,
                "Could not read EDGAR filings of unlisted registrant {Name} (CIK: {Cik}) for listing evidence",
                company.Name,
                company.Cik
            );
            return null;
        }
    }

    private static SecListingEvidenceCheck Record(
        DbContext dbContext,
        SecListingEvidenceCheck check,
        string cik,
        CompanyInfo company,
        SecListingEvidenceResult result,
        DateTime nowUtc,
        ILogger logger
    )
    {
        var wasAdmitted = check?.Outcome == SecListingEvidenceOutcome.Admitted;
        if (check == null)
        {
            check = new SecListingEvidenceCheck { Cik = cik };
            dbContext.Add(check);
        }
        check.Tickers = company.Tickers.ToList();
        check.CheckedAt = nowUtc;
        check.Outcome = result.Outcome;
        check.RegistrationAccessionNumber = result.RegistrationAccessionNumber;
        check.CertificationAccessionNumber = result.CertificationAccessionNumber;
        check.ProspectusAccessionNumber = result.ProspectusAccessionNumber;
        check.ProspectusFiledOn = result.ProspectusFiledOn;

        if (!wasAdmitted && result.Outcome == SecListingEvidenceOutcome.Admitted)
            logger.LogInformation(
                "Admitting {Name} (CIK: {Cik}, tickers {Tickers}) on listing evidence: 8-A12B {Registration}, certification {Certification}, final prospectus {Prospectus}",
                company.Name,
                company.Cik,
                string.Join(",", company.Tickers),
                result.RegistrationAccessionNumber,
                result.CertificationAccessionNumber,
                result.ProspectusAccessionNumber
            );
        return check;
    }
}
