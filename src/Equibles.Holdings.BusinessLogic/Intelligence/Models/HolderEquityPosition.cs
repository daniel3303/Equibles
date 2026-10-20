namespace Equibles.Holdings.BusinessLogic.Intelligence.Models;

/// <summary>
/// A common-share 13F position, without put/call or principal legs. <see cref="Value"/> is null
/// when the row's value is pending or unavailable; the shares still count.
/// <see cref="FiledValue"/> is the filer's own figure, which vouches for the shares while the
/// published value is missing.
/// </summary>
public sealed record HolderEquityPosition(
    HolderSecurityKey Security,
    long Shares,
    long? Value,
    long? FiledValue = null
)
{
    /// <summary>
    /// The value the position reports for its shares: the published value, else the filed one;
    /// null when neither is positive.
    /// </summary>
    public long? ReportedValue =>
        Value is > 0 ? Value
        : FiledValue is > 0 ? FiledValue
        : null;
}
