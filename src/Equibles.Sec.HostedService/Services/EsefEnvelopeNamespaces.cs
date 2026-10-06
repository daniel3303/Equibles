using System.Xml;

namespace Equibles.Sec.HostedService.Services;

internal sealed class EsefEnvelopeNamespaces
{
    internal const string Instance = "http://www.xbrl.org/2003/instance";
    internal const string Dimensions = "http://xbrl.org/2006/xbrldi";
    internal const string Xmlns = "http://www.w3.org/2000/xmlns/";

    private static readonly HashSet<string> ReservedPrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "ix",
        "xbrli",
        "xbrldi",
        "ifrs-full",
        "iso4217",
        "xsi",
        "xml",
    };
    private readonly Dictionary<string, string> bindings = new(StringComparer.OrdinalIgnoreCase);

    public string Prefix(string namespaceUri, string originalPrefix)
    {
        if (string.IsNullOrEmpty(namespaceUri))
            return "";
        var prefix = namespaceUri switch
        {
            Instance => "xbrli",
            Dimensions => "xbrldi",
            "http://www.xbrl.org/2013/inlineXBRL" or "http://www.xbrl.org/2008/inlineXBRL" => "ix",
            "http://www.xbrl.org/2003/iso4217" => "iso4217",
            "http://www.w3.org/2001/XMLSchema-instance" => "xsi",
            "http://www.w3.org/XML/1998/namespace" => "xml",
            _ => Uri.TryCreate(namespaceUri, UriKind.Absolute, out var address)
            && address.Scheme is "http" or "https"
            && address.Host == "xbrl.ifrs.org"
            && address.AbsolutePath.EndsWith("/ifrs-full", StringComparison.Ordinal)
                ? "ifrs-full"
                : null,
        };
        if (prefix == null)
        {
            if (ReservedPrefixes.Contains(originalPrefix))
                throw new InvalidDataException(
                    "A reserved XBRL prefix is bound to an unsupported namespace."
                );
            prefix = originalPrefix;
        }
        if (prefix.Length == 0)
            return prefix;
        if (bindings.TryGetValue(prefix, out var bound) && bound != namespaceUri)
            throw new InvalidDataException(
                "Report mixes namespaces that the shared XBRL parser cannot distinguish."
            );
        bindings[prefix] = namespaceUri;
        return prefix;
    }

    public string QName(string value, XmlReader reader, XmlWriter writer, HashSet<string> declared)
    {
        var parts = value.Trim().Split(':');
        if (parts.Length is < 1 or > 2 || parts.Any(string.IsNullOrEmpty))
            throw new InvalidDataException("Invalid XBRL QName.");
        try
        {
            foreach (var part in parts)
                XmlConvert.VerifyNCName(part);
        }
        catch (XmlException exception)
        {
            throw new InvalidDataException("Invalid XBRL QName.", exception);
        }
        var namespaceUri = reader.LookupNamespace(parts.Length == 2 ? parts[0] : "");
        if (string.IsNullOrEmpty(namespaceUri))
            throw new InvalidDataException("XBRL QName has no resolved namespace.");
        var prefix = Prefix(namespaceUri, parts.Length == 2 ? parts[0] : "");
        if (prefix.Length == 0)
            throw new InvalidDataException(
                "An unsupported unprefixed QName cannot be represented by the shared parser."
            );
        if (prefix != "xml" && declared.Add(prefix))
            writer.WriteAttributeString("xmlns", prefix, Xmlns, namespaceUri);
        return prefix + ":" + parts[^1];
    }
}
