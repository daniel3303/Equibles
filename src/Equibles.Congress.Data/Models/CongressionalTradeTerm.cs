namespace Equibles.Congress.Data.Models;

/// <summary>A trade and the term its member held on the transaction date, if one is known.</summary>
public class CongressionalTradeTerm
{
    public CongressionalTrade Trade { get; set; }

    /// <summary>Null when the member has no BioGuide id or no term covers the date.</summary>
    public CongressMemberTerm Term { get; set; }
}
