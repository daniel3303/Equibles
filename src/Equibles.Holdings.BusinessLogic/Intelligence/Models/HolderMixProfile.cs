using Equibles.Holdings.BusinessLogic.Advisers;
using Equibles.Holdings.Data.Models;

namespace Equibles.Holdings.BusinessLogic.Intelligence.Models;

/// <summary>The regulatory profile fields a filer's holder-mix buckets read.</summary>
public sealed record HolderMixProfile(
    HolderEntityType EntityType,
    HolderClientBase ClientBase,
    bool? AdvisesPrivateFunds,
    PrivateFundFocus PrivateFundFocus
)
{
    /// <summary>
    /// A filer's profile as of one quarter end: its current entity type with the client base and
    /// private funds of the Form ADV roster current then (Unknown and none without one).
    /// </summary>
    public static HolderMixProfile At(HolderEntityType entityType, AdviserFacts adviser) =>
        new(
            entityType,
            adviser?.ClientBase ?? HolderClientBase.Unknown,
            adviser?.AdvisesPrivateFunds,
            adviser?.PrivateFundFocus ?? PrivateFundFocus.None
        );
}
