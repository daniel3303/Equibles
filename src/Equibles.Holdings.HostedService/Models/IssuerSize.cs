namespace Equibles.Holdings.HostedService.Models;

/// <summary>
/// How big the issuer is, as the two figures stored side by side on the stock. Carried through an
/// import so a position can be checked against the company it claims to be a piece of. Both are
/// needed: the share count is the yardstick, and the market cap is the only independent way to
/// tell whether that count is trustworthy (see <c>ImpossiblePositionGuard</c>).
/// </summary>
public record IssuerSize(long SharesOutstanding, double MarketCapitalization);
