using Equibles.CommonStocks.Data.Helpers;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.HostedService.Services;

/// <summary>
/// Chooses the CUSIP a Schedule 13D or 13G position is stored under. A well-formed filed CUSIP
/// decides alone: it is stored when it resolves, and when it resolves to nothing the filing is on a
/// security the directory does not track (foreign ordinary shares behind a depositary share, a
/// preferred, another class), so it stays unresolved. Only a filing with no usable CUSIP (missing,
/// malformed or all zeros) falls back to its subject company CIK, which EDGAR files the schedule
/// under, and then only when that issuer has exactly one security, listed and registered as common
/// shares, whose shares outstanding agree with the filed amount and percent of class.
/// </summary>
internal static class Schedule13DGPositionResolver
{
    /// <summary>
    /// How far, relative to the filed percent of class, the amount over the security's shares
    /// outstanding may stray before the fallback is refused. A depositary share that represents
    /// several ordinary shares, or another class, misses by far more.
    /// </summary>
    public const decimal PercentOfClassTolerance = 0.20m;

    /// <summary>The CUSIP and shares outstanding of a subject company's only security.</summary>
    public sealed record SubjectSecurity(string Cusip, long SharesOutstanding);

    /// <summary>
    /// The filed CUSIP when it resolves to a tracked security, else, for a filing with no usable
    /// CUSIP, the subject company's only listed common security when the lead person's
    /// <paramref name="aggregateAmount"/> and <paramref name="percentOfClass"/> agree with its
    /// shares outstanding, else null.
    /// </summary>
    public static async Task<string> ResolvePositionCusip(
        EquityIssuerRepository issuers,
        string filedCusip,
        string issuerCik,
        long? aggregateAmount,
        decimal? percentOfClass,
        CancellationToken cancellationToken
    )
    {
        var filed = filedCusip?.Trim();
        if (!NeedsSubjectCompany(filed))
        {
            var claims = await HoldingCusipResolution.Load(
                issuers,
                issuers.GetAll(),
                [filed],
                cancellationToken
            );
            return HoldingCusipResolution.Resolve(claims, []).ContainsKey(filed) ? filed : null;
        }

        var canonical = CikNormalizer.Canonicalize(issuerCik);
        if (canonical == null)
            return null;
        var bySubject = await SubjectCompanySecurities(issuers, [canonical], cancellationToken);
        return
            bySubject.TryGetValue(canonical, out var security)
            && AgreesWithSharesOutstanding(
                aggregateAmount,
                percentOfClass,
                security.SharesOutstanding
            )
            ? security.Cusip
            : null;
    }

    /// <summary>
    /// True when the filed CUSIP cannot name a security: missing, failing its check digit, or the
    /// all-zero placeholder. Only such a filing may be mapped through its subject company.
    /// </summary>
    public static bool NeedsSubjectCompany(string filedCusip)
    {
        var cusip = filedCusip?.Trim().ToUpperInvariant();
        return string.IsNullOrEmpty(cusip)
            || !Schedule13DGTextValues.IsValidCusip(cusip)
            || cusip.All(character => character == '0');
    }

    /// <summary>
    /// True when <paramref name="aggregateAmount"/> over <paramref name="sharesOutstanding"/> is
    /// within <see cref="PercentOfClassTolerance"/> of the filed <paramref name="percentOfClass"/>.
    /// Anything that cannot be checked disagrees: no percent, no shares outstanding, or a zero
    /// stake, which matches every class alike.
    /// </summary>
    public static bool AgreesWithSharesOutstanding(
        long? aggregateAmount,
        decimal? percentOfClass,
        long sharesOutstanding
    )
    {
        if (aggregateAmount is not { } amount || percentOfClass is not { } filed)
            return false;
        if (sharesOutstanding <= 0 || amount <= 0 || filed <= 0)
            return false;
        var implied = amount * 100m / sharesOutstanding;
        return Math.Abs(implied - filed) <= filed * PercentOfClassTolerance;
    }

    /// <summary>
    /// For each subject company CIK (canonical, without leading zeros), its only security when
    /// exactly one issuer carries that CIK, the issuer has no other security, that security is
    /// listed and registered as common shares, and its CUSIP resolves back to the issuer. CIKs that
    /// fail any condition are absent.
    /// </summary>
    public static async Task<Dictionary<string, SubjectSecurity>> SubjectCompanySecurities(
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
                    .Securities.Where(security =>
                        security.Listings.Any() || security.Cusip != null
                    )
                    .Select(security => new
                    {
                        security.Cusip,
                        security.RegistrationType,
                        security.SharesOutstanding,
                        Listed = security.Listings.Any(),
                    })
                    .ToList(),
            })
            .ToListAsync(cancellationToken);

        var candidates = new Dictionary<string, (Guid IssuerId, SubjectSecurity Security)>();
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
            candidates[group.Key] = (
                issuer.Id,
                new SubjectSecurity(security.Cusip, security.SharesOutstanding)
            );
        }

        if (candidates.Count == 0)
            return [];

        var claims = await HoldingCusipResolution.Load(
            issuers,
            issuers.GetAll(),
            candidates.Values.Select(candidate => candidate.Security.Cusip).ToList(),
            cancellationToken
        );
        var resolved = HoldingCusipResolution.Resolve(claims, []);
        return candidates
            .Where(candidate =>
                resolved.TryGetValue(candidate.Value.Security.Cusip, out var target)
                && target.CommonStockId == candidate.Value.IssuerId
            )
            .ToDictionary(candidate => candidate.Key, candidate => candidate.Value.Security);
    }
}
