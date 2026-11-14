using Equibles.InsiderTrading.Data.Models;

namespace Equibles.InsiderTrading.Repositories;

/// <summary>
/// An insider's relationship to the issuer as one label. The boxes ticked on the filing decide
/// when the row records them, because one person can be a director of one company and an officer
/// of another; the owner's first-seen flags stand in only for rows read before the boxes were kept.
/// </summary>
public static class InsiderRoleLabel
{
    public static string Of(InsiderOwner owner, InsiderRelationship? filed = null)
    {
        var relationship = filed ?? FirstSeen(owner);
        var roles = new List<string>();
        if (relationship.HasFlag(InsiderRelationship.Director))
            roles.Add("Director");
        if (relationship.HasFlag(InsiderRelationship.Officer))
            roles.Add(
                string.IsNullOrWhiteSpace(owner.OfficerTitle) ? "Officer" : owner.OfficerTitle
            );
        if (relationship.HasFlag(InsiderRelationship.TenPercentOwner))
            roles.Add("10% Owner");
        return roles.Count > 0 ? string.Join(", ", roles) : "Insider";
    }

    private static InsiderRelationship FirstSeen(InsiderOwner owner) =>
        (owner.IsDirector ? InsiderRelationship.Director : InsiderRelationship.None)
        | (owner.IsOfficer ? InsiderRelationship.Officer : InsiderRelationship.None)
        | (
            owner.IsTenPercentOwner ? InsiderRelationship.TenPercentOwner : InsiderRelationship.None
        );
}
