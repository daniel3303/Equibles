namespace Equibles.Sec.FinancialFacts.BusinessLogic.Parsers;

// Engine-agnostic XBRL value-parsing primitives shared by StandaloneXbrlParser
// (LINQ-to-XML) and InlineXbrlParser (AngleSharp): both operate on the same string
// attribute/QName values, so this logic stays identical across the two engines.
internal static class XbrlValueParser
{
    // Returns null when the QName has a prefix but an empty local name
    // (e.g. "iso4217:"); such a measure is unresolvable.
    public static string StripPrefix(string qname)
    {
        var colonIdx = qname.IndexOf(':');
        if (colonIdx < 0)
            return qname;
        var local = qname.Substring(colonIdx + 1);
        return local.Length == 0 ? null : local;
    }

    public static int? ParseDecimals(string decimalsAttribute)
    {
        if (string.IsNullOrEmpty(decimalsAttribute))
            return null;
        if (string.Equals(decimalsAttribute, "INF", StringComparison.OrdinalIgnoreCase))
            return int.MaxValue;
        return int.TryParse(decimalsAttribute, out var value) ? value : null;
    }
}
