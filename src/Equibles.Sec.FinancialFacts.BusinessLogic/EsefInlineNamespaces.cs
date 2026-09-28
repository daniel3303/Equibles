using System.Xml;

namespace Equibles.Sec.FinancialFacts.BusinessLogic;

internal static class EsefInlineNamespaces
{
    public static bool AreUnambiguous(string html)
    {
        var bindings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var input = new StringReader(html);
            using var reader = XmlReader.Create(input, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Ignore,
                XmlResolver = null,
                MaxCharactersInDocument = 50 * 1024 * 1024
            });
            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element)
                    continue;
                if (reader.Depth == 0
                    && (reader.LocalName != "html"
                        || reader.NamespaceURI != "http://www.w3.org/1999/xhtml"))
                    return false;

                // The shared HTML parser has one prefix map for the entire document.
                // A scoped rebinding cannot be represented faithfully in that map.
                while (reader.MoveToNextAttribute())
                {
                    if (reader.Prefix != "xmlns" && reader.Name != "xmlns")
                        continue;
                    var prefix = reader.Prefix == "xmlns" ? reader.LocalName : "";
                    var value = reader.Value;
                    if (!HasSupportedPrefix(prefix, value))
                        return false;
                    if (prefix.Length == 0)
                        continue;
                    if ((bindings.TryGetValue(prefix, out var previous) && previous != value)
                        || !ValidReservedBinding(prefix, value))
                        return false;
                    bindings[prefix] = value;
                }
                reader.MoveToElement();
                if (reader.NamespaceURI == "http://www.xbrl.org/2003/instance"
                    && reader.LocalName == "measure" && !HasSupportedMeasure(reader))
                    return false;
                if (string.Equals(reader.Prefix, "ix", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(reader.LocalName, "nonFraction", StringComparison.OrdinalIgnoreCase))
                {
                    var name = reader.GetAttribute("name")?.Split(':');
                    if (name?.Length != 2 || string.IsNullOrEmpty(reader.LookupNamespace(name[0])))
                        return false;
                }
            }
            return bindings.ContainsKey("ix") && bindings.ContainsKey("xbrli");
        }
        catch (XmlException)
        {
            return false;
        }
    }

    private static bool HasSupportedMeasure(XmlReader reader)
    {
        var namespaces = ((IXmlNamespaceResolver)reader).GetNamespacesInScope(XmlNamespaceScope.All);
        using var measure = reader.ReadSubtree();
        measure.MoveToContent();
        var parts = measure.ReadElementContentAsString().Trim().Split(':');
        if (parts.Length != 2 || !namespaces.TryGetValue(parts[0], out var ns))
            return false;
        XmlConvert.VerifyNCName(parts[0]);
        XmlConvert.VerifyNCName(parts[1]);
        // The shared parser drops unit prefixes. Only these namespaces give that
        // abbreviated unit an unambiguous currency, ratio or share-count identity.
        return ns == "http://www.xbrl.org/2003/iso4217"
            || (ns == "http://www.xbrl.org/2003/instance" && parts[1] is "pure" or "shares");
    }

    // The HTML parser identifies these elements by their literal prefixes. An alias
    // could hide a segment/scenario qualifier and turn it into consolidated evidence.
    private static bool HasSupportedPrefix(string prefix, string value)
    {
        var expected = value switch
        {
            "http://www.xbrl.org/2013/inlineXBRL" or "http://www.xbrl.org/2008/inlineXBRL" => "ix",
            "http://www.xbrl.org/2003/instance" => "xbrli",
            "http://xbrl.org/2006/xbrldi" => "xbrldi",
            "http://www.xbrl.org/2003/iso4217" => "iso4217",
            _ => null
        };
        return expected == null || string.Equals(prefix, expected, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ValidReservedBinding(string prefix, string value) => prefix.ToLowerInvariant() switch
    {
        "ix" => value is "http://www.xbrl.org/2013/inlineXBRL"
            or "http://www.xbrl.org/2008/inlineXBRL",
        "xbrli" => value == "http://www.xbrl.org/2003/instance",
        "xbrldi" => value == "http://xbrl.org/2006/xbrldi",
        "iso4217" => value == "http://www.xbrl.org/2003/iso4217",
        _ => true
    };
}
