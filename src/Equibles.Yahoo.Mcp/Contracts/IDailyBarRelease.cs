namespace Equibles.Yahoo.Mcp.Contracts;

/// <summary>
/// Optional hold on the newest daily session for the caller being served. A deployment that
/// releases a session's bars to some callers later than to others registers an implementation;
/// nothing is registered by default, and every stored bar stays readable.
/// </summary>
public interface IDailyBarRelease
{
    /// <summary>
    /// The session withheld from the current caller, or <c>null</c> when every stored bar is
    /// released to them.
    /// </summary>
    Task<DailyBarHold> GetHold(CancellationToken cancellationToken = default);
}

/// <summary>
/// A session the current caller cannot read yet. Bars dated on or after <paramref name="Session"/>
/// are left out of every price tool, and <paramref name="Note"/> is appended verbatim below a
/// table whose range reaches that session, so the caller learns why its newest row is older.
/// </summary>
public sealed record DailyBarHold(DateOnly Session, string Note);
