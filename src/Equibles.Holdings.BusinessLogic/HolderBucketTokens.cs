using Equibles.Holdings.Data.Models;

namespace Equibles.Holdings.BusinessLogic;

/// <summary>
/// The short URL and tool-argument tokens for holder entity types, with the compact labels tables
/// print. Every surface parses holder-type filters here, so an unknown token is refused the same
/// way everywhere.
/// </summary>
public static class HolderBucketTokens
{
    private static readonly (string Token, HolderEntityType Type, string Label)[] EntityTypes =
    [
        ("adv", HolderEntityType.RegisteredAdviser, "Registered adviser"),
        ("era", HolderEntityType.ExemptReportingAdviser, "Exempt adviser"),
        ("ia", HolderEntityType.InvestmentAdviser, "Adviser"),
        ("bank", HolderEntityType.Bank, "Bank"),
        ("ins", HolderEntityType.InsuranceCompany, "Insurer"),
        ("bd", HolderEntityType.BrokerDealer, "Broker-dealer"),
        ("fund", HolderEntityType.RegisteredFund, "Registered fund"),
        ("plan", HolderEntityType.BenefitPlan, "Benefit plan"),
        ("nonus", HolderEntityType.NonUsInstitution, "Non-US"),
        ("unk", HolderEntityType.Unknown, "Unknown"),
    ];

    public static string EntityTypeTokenList =>
        string.Join(", ", EntityTypes.Select(entry => entry.Token));

    public static string Token(HolderEntityType type) =>
        EntityTypes.Single(entry => entry.Type == type).Token;

    // The selected types' tokens in the accepted-token order, for echoing a filter back.
    public static string Describe(IReadOnlySet<HolderEntityType> types) =>
        string.Join(
            ", ",
            EntityTypes.Where(entry => types.Contains(entry.Type)).Select(entry => entry.Token)
        );

    // A holder not yet profiled prints a dash; Unknown means its records decide no type.
    public static string Label(HolderEntityType? type) =>
        type == null ? "—" : EntityTypes.Single(entry => entry.Type == type).Label;

    /// <summary>
    /// Parses comma-separated entity type tokens, case-insensitively. Blank input means no filter
    /// (null types); any unknown token returns an error naming the accepted tokens.
    /// </summary>
    public static (IReadOnlySet<HolderEntityType> Types, string Error) ParseEntityTypes(
        string tokens
    ) => ParseEntityTypes(tokens == null ? null : [tokens]);

    /// <summary>
    /// Parses entity type tokens given one per element; an element may also hold several
    /// comma-separated tokens.
    /// </summary>
    public static (IReadOnlySet<HolderEntityType> Types, string Error) ParseEntityTypes(
        IEnumerable<string> tokens
    )
    {
        if (tokens == null)
            return (null, null);
        var types = new HashSet<HolderEntityType>();
        foreach (
            var token in tokens
                .Where(element => element != null)
                .SelectMany(element =>
                    element.Split(
                        ',',
                        StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries
                    )
                )
        )
        {
            var match = EntityTypes.FirstOrDefault(entry =>
                string.Equals(entry.Token, token, StringComparison.OrdinalIgnoreCase)
            );
            if (match.Token == null)
                return (
                    null,
                    $"Unknown holder type '{token}'. Use one or more of: {EntityTypeTokenList}."
                );
            types.Add(match.Type);
        }
        return (types.Count == 0 ? null : types, null);
    }
}
