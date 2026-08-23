namespace Equibles.Congress.Data.Models;

public sealed record CongressMemberIdentity(
    string BioguideId,
    string CanonicalName,
    IReadOnlyList<string> Aliases
);
