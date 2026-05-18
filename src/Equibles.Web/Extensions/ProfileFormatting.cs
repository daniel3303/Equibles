namespace Equibles.Web.Extensions;

/// <summary>Small presentation helpers shared by the entity profile pages.</summary>
public static class ProfileFormatting
{
    public static string JoinLocation(string city, string stateOrCountry)
    {
        return string.Join(
            ", ",
            new[] { city, stateOrCountry }.Where(part => !string.IsNullOrWhiteSpace(part))
        );
    }

    public static string DescribeRole(
        string officerTitle,
        bool isDirector,
        bool isTenPercentOwner
    )
    {
        if (!string.IsNullOrWhiteSpace(officerTitle))
            return officerTitle;
        if (isDirector)
            return "Director";
        if (isTenPercentOwner)
            return "10% owner";
        return null;
    }
}
