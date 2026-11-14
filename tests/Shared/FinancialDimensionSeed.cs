using System.Security.Cryptography;
using System.Text;
using Equibles.Sec.FinancialFacts.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.TestSupport;

// A fact carries only the key of its dimension set; the pairs are stored once per key.
internal static class FinancialDimensionSeed
{
    /// <summary>
    /// The key for these (axis, member) pairs, hashed as production does, adding the set to the
    /// context unless it already holds it. No pairs is the consolidated (empty) key.
    /// </summary>
    public static string Key(DbContext db, params (string Axis, string Member)[] pairs)
    {
        if (pairs.Length == 0)
            return "";
        var canonical = string.Join(
            "|",
            pairs.Select(pair => $"{pair.Axis}={pair.Member}").OrderBy(p => p, StringComparer.Ordinal)
        );
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();
        if (db.Set<FinancialDimensionSet>().Find(key) == null)
            db.Add(
                new FinancialDimensionSet
                {
                    Key = key,
                    Members = pairs
                        .Distinct()
                        .Select(pair => new FinancialDimensionSetMember
                        {
                            DimensionSetKey = key,
                            Axis = pair.Axis,
                            Member = pair.Member,
                        })
                        .ToList(),
                }
            );
        return key;
    }
}
