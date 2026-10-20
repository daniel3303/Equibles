using Equibles.InsiderTrading.Data.Models;

namespace Equibles.InsiderTrading.Repositories;

/// <summary>An insider's relationship to the issuer as one label, from the owner's Form 3/4/5 flags.</summary>
public static class InsiderRoleLabel
{
    public static string Of(InsiderOwner owner)
    {
        var roles = new List<string>();
        if (owner.IsDirector)
            roles.Add("Director");
        if (owner.IsOfficer)
            roles.Add(
                string.IsNullOrWhiteSpace(owner.OfficerTitle) ? "Officer" : owner.OfficerTitle
            );
        if (owner.IsTenPercentOwner)
            roles.Add("10% Owner");
        return roles.Count > 0 ? string.Join(", ", roles) : "Insider";
    }
}
