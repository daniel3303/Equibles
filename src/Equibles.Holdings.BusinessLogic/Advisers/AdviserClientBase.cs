using Equibles.Holdings.Data.Models;
using Equibles.Sec.Data.Models;

namespace Equibles.Holdings.BusinessLogic.Advisers;

/// <summary>
/// Groups an adviser's Form ADV Item 5.D client types into a <see cref="HolderClientBase"/>: the
/// group holding at least half of the regulatory assets, else Diversified; Unknown when no
/// assets are reported (an exempt reporting adviser files no Item 5).
/// </summary>
public static class AdviserClientBase
{
    private static readonly IReadOnlyDictionary<FormAdvClientType, HolderClientBase> Groups =
        new Dictionary<FormAdvClientType, HolderClientBase>
        {
            [FormAdvClientType.Individuals] = HolderClientBase.Individuals,
            [FormAdvClientType.HighNetWorthIndividuals] = HolderClientBase.Individuals,
            [FormAdvClientType.BanksOrThrifts] = HolderClientBase.BanksAndThrifts,
            [FormAdvClientType.InvestmentCompanies] = HolderClientBase.RegisteredFunds,
            [FormAdvClientType.BusinessDevelopmentCompanies] = HolderClientBase.RegisteredFunds,
            [FormAdvClientType.PooledInvestmentVehicles] =
                HolderClientBase.PooledInvestmentVehicles,
            [FormAdvClientType.PensionAndProfitSharingPlans] = HolderClientBase.PensionPlans,
            [FormAdvClientType.CharitableOrganizations] = HolderClientBase.CharitableOrganizations,
            [FormAdvClientType.GovernmentEntities] = HolderClientBase.GovernmentAndSovereign,
            [FormAdvClientType.SovereignWealthFunds] = HolderClientBase.GovernmentAndSovereign,
            [FormAdvClientType.OtherInvestmentAdvisers] = HolderClientBase.OtherInvestmentAdvisers,
            [FormAdvClientType.InsuranceCompanies] = HolderClientBase.InsuranceCompanies,
            [FormAdvClientType.Corporations] = HolderClientBase.Corporations,
            [FormAdvClientType.Other] = HolderClientBase.Other,
        };

    /// <summary>The client base, its share of regulatory assets and the pooled vehicles' share.</summary>
    public static (HolderClientBase Base, decimal? Share, decimal? PooledShare) Of(
        IEnumerable<(FormAdvClientType ClientType, long? RegulatoryAum)> clientTypes
    )
    {
        var rows = clientTypes.ToList();
        var total = rows.Sum(row => row.RegulatoryAum ?? 0);
        if (total <= 0)
            return (HolderClientBase.Unknown, null, null);

        var shares = rows.Where(row => Groups.ContainsKey(row.ClientType))
            .GroupBy(row => Groups[row.ClientType])
            .ToDictionary(
                group => group.Key,
                group => (decimal)group.Sum(row => row.RegulatoryAum ?? 0) / total
            );
        var pooled = shares.GetValueOrDefault(HolderClientBase.PooledInvestmentVehicles);
        var majority = shares.Where(pair => pair.Value >= 0.5m).ToList();
        return majority.Count == 1
            ? (majority[0].Key, majority[0].Value, pooled)
            : (HolderClientBase.Diversified, null, pooled);
    }
}
