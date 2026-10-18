using Equibles.Holdings.BusinessLogic.Intelligence.Models;
using Equibles.Holdings.Data.Models;

namespace Equibles.Holdings.BusinessLogic;

/// <summary>
/// The short URL and tool-argument tokens for holder entity types and style bands, with the
/// compact labels tables print. Every surface parses these filters here, so an unknown token is
/// refused the same way everywhere.
/// </summary>
public static class HolderBucketTokens
{
    private static readonly (string Token, HolderEntityType Value, string Label)[] EntityTypes =
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

    private static readonly (string Token, HolderStyleBand Value, string Label)[] StyleBands =
    [
        ("idx", HolderStyleBand.IndexLike, "Index-like"),
        ("sys", HolderStyleBand.SystematicLike, "Systematic-like"),
        ("conc", HolderStyleBand.Concentrated, "Concentrated"),
        ("divact", HolderStyleBand.DiversifiedActive, "Diversified active"),
        ("unk", HolderStyleBand.Unknown, "Unknown"),
    ];

    private static readonly (
        string Token,
        HolderMixDimension Value,
        string Label
    )[] MixDimensions =
    [
        ("type", HolderMixDimension.EntityType, "Holder type"),
        ("style", HolderMixDimension.Style, "Style"),
        ("turnover", HolderMixDimension.Turnover, "Turnover"),
        ("horizon", HolderMixDimension.Horizon, "Holding horizon"),
        ("aum", HolderMixDimension.BookSize, "13F book size"),
        ("client", HolderMixDimension.ClientBase, "Client base"),
        ("pf", HolderMixDimension.PrivateFunds, "Private funds"),
        ("country", HolderMixDimension.Location, "Location"),
    ];

    public static string EntityTypeTokenList => TokenList(EntityTypes);

    public static string MixDimensionTokenList => TokenList(MixDimensions);

    public static string Token(HolderMixDimension dimension) =>
        MixDimensions.Single(entry => entry.Value == dimension).Token;

    public static string Label(HolderMixDimension dimension) =>
        MixDimensions.Single(entry => entry.Value == dimension).Label;

    /// <summary>
    /// Parses one holder-mix dimension token, case-insensitively. Blank input means the default
    /// (null dimension); an unknown token returns an error naming the accepted tokens.
    /// </summary>
    public static (HolderMixDimension? Dimension, string Error) ParseMixDimension(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return (null, null);
        var match = MixDimensions.FirstOrDefault(entry =>
            string.Equals(entry.Token, token.Trim(), StringComparison.OrdinalIgnoreCase)
        );
        return match.Token == null
            ? (null, $"Unknown dimension '{token.Trim()}'. Use one of: {MixDimensionTokenList}.")
            : (match.Value, null);
    }

    public static string StyleTokenList => TokenList(StyleBands);

    public static string Token(HolderEntityType type) =>
        EntityTypes.Single(entry => entry.Value == type).Token;

    public static string StyleToken(HolderStyleBand style) =>
        StyleBands.Single(entry => entry.Value == style).Token;

    // The selected types' tokens in the accepted-token order, for echoing a filter back.
    public static string Describe(IReadOnlySet<HolderEntityType> types) =>
        Describe(EntityTypes, types);

    public static string Describe(IReadOnlySet<HolderStyleBand> styles) =>
        Describe(StyleBands, styles);

    // A holder not yet profiled prints a dash; Unknown means its records decide no type.
    public static string Label(HolderEntityType? type) =>
        type == null ? "—" : EntityTypes.Single(entry => entry.Value == type).Label;

    // A holder not yet measured prints a dash; Unknown means its book is too small or unvalued to band.
    public static string StyleLabel(HolderStyleBand? style) =>
        style == null ? "—" : StyleBands.Single(entry => entry.Value == style).Label;

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
    ) => Parse(EntityTypes, tokens, "holder type");

    /// <summary>Parses style band tokens the way <see cref="ParseEntityTypes(IEnumerable{string})"/> does.</summary>
    public static (IReadOnlySet<HolderStyleBand> Styles, string Error) ParseStyles(
        IEnumerable<string> tokens
    ) => Parse(StyleBands, tokens, "style");

    private static string TokenList<T>((string Token, T Value, string Label)[] table) =>
        string.Join(", ", table.Select(entry => entry.Token));

    private static string Describe<T>(
        (string Token, T Value, string Label)[] table,
        IReadOnlySet<T> selected
    ) =>
        string.Join(
            ", ",
            table.Where(entry => selected.Contains(entry.Value)).Select(entry => entry.Token)
        );

    private static (IReadOnlySet<T> Values, string Error) Parse<T>(
        (string Token, T Value, string Label)[] table,
        IEnumerable<string> tokens,
        string kind
    )
    {
        if (tokens == null)
            return (null, null);
        var values = new HashSet<T>();
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
            var match = table.FirstOrDefault(entry =>
                string.Equals(entry.Token, token, StringComparison.OrdinalIgnoreCase)
            );
            if (match.Token == null)
                return (null, $"Unknown {kind} '{token}'. Use one or more of: {TokenList(table)}.");
            values.Add(match.Value);
        }
        return (values.Count == 0 ? null : values, null);
    }
}
