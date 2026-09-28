using Equibles.Sec.FinancialFacts.BusinessLogic.Models;

namespace Equibles.Sec.FinancialFacts.BusinessLogic.Parsers;

// ESEF is XML. The shared HTML parser is also used for tolerant SEC envelopes,
// so only this entry point can establish faithful European namespace evidence.
public static class EsefInlineXbrlParser
{
    public static List<ParsedXbrlFact> Parse(string html)
    {
        if (!TryParse(html, out var facts))
            throw new InvalidDataException(
                "ESEF inline namespace bindings cannot be represented faithfully by the parser."
            );
        return facts;
    }

    public static bool TryParse(string html, out List<ParsedXbrlFact> facts)
    {
        facts = [];
        // UTF-8 byte decoders preserve the signature; StringReader expects XML text.
        if (html?.StartsWith('\uFEFF') == true)
            html = html[1..];
        if (string.IsNullOrWhiteSpace(html) || !EsefInlineNamespaces.AreUnambiguous(html))
            return false;
        facts = new InlineXbrlParser().Parse(html);
        return true;
    }
}
