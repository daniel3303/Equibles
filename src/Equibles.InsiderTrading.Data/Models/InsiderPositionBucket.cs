namespace Equibles.InsiderTrading.Data.Models;

/// <summary>
/// One balance within an insider's filing: a security held directly, or held indirectly through
/// one vehicle, named by the row's filed nature of ownership and its footnote ids. A filing lists
/// intermediate balances per bucket, so each bucket's last row in filing order is its closing
/// balance. Rows parsed before version 13 carry no vehicle and share one indirect bucket.
/// </summary>
public readonly record struct InsiderPositionBucket(
    string SecurityTitle,
    OwnershipNature OwnershipNature,
    string Vehicle
)
{
    public static InsiderPositionBucket Of(InsiderTransaction row) =>
        new(
            row.SecurityTitle ?? "",
            row.OwnershipNature,
            row.OwnershipNature == OwnershipNature.Direct
                ? ""
                : $"{row.NatureOfOwnership}\u001f{string.Join(',', row.OwnershipNatureFootnoteIds ?? [])}"
        );
}
