using Equibles.Holdings.Data.Models;

namespace Equibles.Holdings.BusinessLogic.Intelligence.Models;

/// <summary>The regulatory profile fields a filer's holder-mix buckets read.</summary>
public sealed record HolderMixProfile(
    HolderEntityType EntityType,
    HolderClientBase ClientBase,
    bool? AdvisesPrivateFunds,
    PrivateFundFocus PrivateFundFocus
);
