using System.Security.Cryptography;
using System.Text;
using Equibles.Sec.FinancialFacts.BusinessLogic.Models;

namespace Equibles.Sec.FinancialFacts.BusinessLogic.Parsers;

/// <summary>
/// Computes the canonical <c>FinancialFact.DimensionsKey</c> fingerprint for a
/// parsed fact's explicit XBRL dimensions: the empty string for the
/// consolidated (no-dimension) context, otherwise the lowercase hex SHA-256 of
/// the ordinal-sorted <c>axis=member|axis=member</c> QName pairs. Sorting makes
/// the key independent of the order the source document declared the members
/// in, so the same dimensional cut always lands on the same unique-index slot
/// regardless of filing-to-filing markup differences.
/// </summary>
public static class XbrlDimensionsKey
{
    public static string Compute(IReadOnlyCollection<ParsedXbrlDimension> dimensions)
    {
        if (dimensions == null || dimensions.Count == 0)
            return string.Empty;

        var canonical = string.Join(
            "|",
            dimensions
                .Select(d => $"{d.Axis}={d.Member}")
                .OrderBy(pair => pair, StringComparer.Ordinal)
        );

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
