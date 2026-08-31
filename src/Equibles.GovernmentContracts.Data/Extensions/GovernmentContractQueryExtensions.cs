using Equibles.GovernmentContracts.Data.Models;

namespace Equibles.GovernmentContracts.Data.Extensions;

public static class GovernmentContractQueryExtensions
{
    public static IOrderedQueryable<GovernmentContract> OrderForPublicSurface(
        this IQueryable<GovernmentContract> query,
        bool sortByDate
    ) =>
        sortByDate
            ? query
                .OrderByDescending(contract => contract.ActionDate)
                .ThenByDescending(contract => contract.Amount)
                .ThenBy(contract => contract.AwardUniqueKey)
            : query
                .OrderByDescending(contract => contract.Amount)
                .ThenByDescending(contract => contract.ActionDate)
                .ThenBy(contract => contract.AwardUniqueKey);
}
