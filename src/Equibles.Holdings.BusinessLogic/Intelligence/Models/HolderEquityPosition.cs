namespace Equibles.Holdings.BusinessLogic.Intelligence.Models;

/// <summary>
/// A common-share 13F position, without put/call or principal legs. <see cref="Value"/> is null
/// when the row's value is pending or unavailable; the shares still count.
/// </summary>
public sealed record HolderEquityPosition(HolderSecurityKey Security, long Shares, long? Value);
