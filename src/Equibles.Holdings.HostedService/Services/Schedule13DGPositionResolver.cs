using Equibles.CommonStocks.Data.Helpers;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.HostedService.Services;

/// <summary>
/// Chooses the CUSIP a Schedule 13D or 13G position is stored under. The filed CUSIP wins when it
/// resolves. Otherwise the filing's subject company CIK, which EDGAR files the schedule under,
/// names the issuer, and the class is certain only when that issuer has exactly one security, it
/// is listed, and its registration says common shares. Anything else stays unresolved.
/// </summary>
internal static class Schedule13DGPositionResolver
{
    /// <summary>
    /// The filed CUSIP when it resolves to a tracked security, else the subject company's only
    /// listed common security's CUSIP, else null.
    /// </summary>
    public static async Task<string> ResolvePositionCusip(
        EquityIssuerRepository issuers,
        string filedCusip,
        string issuerCik,
        CancellationToken cancellationToken
    )
    {
        var filed = filedCusip?.Trim();
        if (!string.IsNullOrEmpty(filed))
        {
            var claims = await HoldingCusipResolution.Load(
                issuers,
                issuers.GetAll(),
                [filed],
                cancellationToken
            );
            if (HoldingCusipResolution.Resolve(claims, []).ContainsKey(filed))
                return filed;
        }

        var canonical = CikNormalizer.Canonicalize(issuerCik);
        if (canonical == null)
            return null;
        var bySubject = await SubjectCompanyCusips(issuers, [canonical], cancellationToken);
        return bySubject.GetValueOrDefault(canonical);
    }

    /// <summary>
    /// For each subject company CIK (canonical, without leading zeros), the CUSIP of its only
    /// security when exactly one issuer carries that CIK, the issuer has no other security, that
    /// security is listed and registered as common shares, and its CUSIP resolves back to the
    /// issuer. CIKs that fail any condition are absent.
    /// </summary>
    public static async Task<Dictionary<string, string>> SubjectCompanyCusips(
        EquityIssuerRepository issuers,
        IReadOnlyCollection<string> issuerCiks,
        CancellationToken cancellationToken
    )
    {
        var canonical = issuerCiks
            .Select(CikNormalizer.Canonicalize)
            .Where(cik => cik != null)
            .Distinct()
            .ToList();
        if (canonical.Count == 0)
            return [];

        // Stored CIKs keep their source padding.
        var spellings = canonical.Concat(canonical.Select(cik => cik.PadLeft(10, '0'))).ToList();
        var rows = await issuers
            .GetAll()
            .Where(issuer => spellings.Contains(issuer.Cik))
            .Select(issuer => new
            {
                issuer.Id,
                issuer.Cik,
                // A retained security with a CUSIP may be another class, so it counts.
                Securities = issuer
                    .Securities.Where(security => security.Listings.Any() || security.Cusip != null)
                    .Select(security => new
                    {
                        security.Cusip,
                        security.RegistrationType,
                        Listed = security.Listings.Any(),
                    })
                    .ToList(),
            })
            .ToListAsync(cancellationToken);

        var candidates = new Dictionary<string, (Guid IssuerId, string Cusip)>();
        foreach (var group in rows.GroupBy(row => CikNormalizer.Canonicalize(row.Cik)))
        {
            if (group.Key == null || group.Count() != 1)
                continue;
            var issuer = group.Single();
            if (issuer.Securities.Count != 1)
                continue;
            var security = issuer.Securities[0];
            if (
                !security.Listed
                || security.RegistrationType != ListedSecurityType.CommonShares
                || string.IsNullOrWhiteSpace(security.Cusip)
            )
                continue;
            candidates[group.Key] = (issuer.Id, security.Cusip);
        }

        if (candidates.Count == 0)
            return [];

        var claims = await HoldingCusipResolution.Load(
            issuers,
            issuers.GetAll(),
            candidates.Values.Select(candidate => candidate.Cusip).ToList(),
            cancellationToken
        );
        var resolved = HoldingCusipResolution.Resolve(claims, []);
        return candidates
            .Where(candidate =>
                resolved.TryGetValue(candidate.Value.Cusip, out var target)
                && target.CommonStockId == candidate.Value.IssuerId
            )
            .ToDictionary(candidate => candidate.Key, candidate => candidate.Value.Cusip);
    }
}
