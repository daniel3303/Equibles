namespace Equibles.Congress.Data.Models;

/// <summary>
/// Reviewed, exact filing-name aliases backed by official BioGuide identities. This catalog is
/// deliberately exact: an unknown spelling remains unresolved instead of being joined by name
/// similarity. Add an alias only after verifying the member's BioGuide id against an official
/// House Clerk or Congress.gov record.
/// </summary>
public static class CongressMemberIdentityCatalog
{
    private static readonly IReadOnlyList<CongressMemberIdentity> Identities =
    [
        Identity("S001150", "Adam Schiff", "Adam B Schiff", "Adam B. Schiff"),
        Identity(
            "O000172",
            "Alexandria Ocasio-Cortez",
            "Alexandria F Ocasio-Cortez",
            "Alexandria F. Ocasio-Cortez"
        ),
        Identity("R000305", "Deborah Ross", "Deborah K Ross", "Deborah K. Ross"),
        Identity("B001299", "James Banks", "James E Banks", "James E. Banks", "Jim Banks"),
        Identity("C001114", "John Curtis", "John R Curtis", "John R. Curtis"),
        Identity(
            "M001239",
            "John McGuire",
            "John J McGuire",
            "John J. McGuire",
            "John McGuire III",
            "John J. McGuire III"
        ),
        Identity("B001275", "Larry Bucshon", "Larry D Bucshon", "Larry D. Bucshon"),
        Identity("M001136", "Lisa McClain", "Lisa C McClain", "Lisa C. McClain"),
        Identity("M001211", "Mary Miller", "Mary E Miller", "Mary E. Miller"),
        Identity(
            "R000103",
            "Matt Rosendale",
            "Matt M Rosendale",
            "Matt M. Rosendale",
            "Matthew Rosendale"
        ),
        Identity("G000595", "Robert Good", "Robert G Good", "Robert G. Good"),
        Identity("M001198", "Roger Marshall", "Roger W Marshall", "Roger W. Marshall"),
        Identity(
            "Y000067",
            "Rudy Yakym",
            "Rudy C Yakym",
            "Rudy C. Yakym",
            "Rudy Yakym III",
            "Rudy C. Yakym III"
        ),
        Identity("F000471", "Scott Fitzgerald", "Scott L Fitzgerald", "Scott L. Fitzgerald"),
        Identity("F000472", "Scott Franklin", "C Scott Franklin", "C. Scott Franklin"),
        Identity("B001313", "Shontel Brown", "M Shontel Brown", "Shontel M. Brown"),
        Identity("G000587", "Sylvia Garcia", "Sylvia R Garcia", "Sylvia R. Garcia"),
        Identity("B001305", "Ted Budd", "Theodore Budd", "Theodore P Budd", "Theodore P. Budd"),
        Identity("S001201", "Thomas Suozzi", "Thomas R Suozzi", "Thomas R. Suozzi"),
    ];

    private static readonly IReadOnlyDictionary<string, CongressMemberIdentity> ByAlias =
        BuildAliasIndex();

    public static IReadOnlyList<CongressMemberIdentity> All => Identities;

    public static CongressMemberIdentity Resolve(string normalizedFilingName) =>
        normalizedFilingName != null
            ? ByAlias.GetValueOrDefault(normalizedFilingName.Trim())
            : null;

    private static CongressMemberIdentity Identity(
        string bioguideId,
        string canonicalName,
        params string[] aliases
    ) =>
        new(
            bioguideId,
            canonicalName,
            aliases.Prepend(canonicalName).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
        );

    private static IReadOnlyDictionary<string, CongressMemberIdentity> BuildAliasIndex()
    {
        var aliases = new Dictionary<string, CongressMemberIdentity>(
            StringComparer.OrdinalIgnoreCase
        );
        foreach (var identity in Identities)
        foreach (var alias in identity.Aliases)
        {
            if (!aliases.TryAdd(alias, identity))
                throw new InvalidOperationException(
                    $"Congress member alias '{alias}' is ambiguous"
                );
        }

        return aliases;
    }
}
