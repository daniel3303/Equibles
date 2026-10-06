using System.Text;
using System.Xml;
using static Equibles.Sec.HostedService.Services.EsefEnvelopeNamespaces;

namespace Equibles.Sec.HostedService.Services;

internal sealed class EsefEnvelopeWriter(XmlReader reader, XmlWriter writer, bool canonicalize)
{
    private const string Xhtml = "http://www.w3.org/1999/xhtml";
    private const int MaximumDepth = 256;
    private readonly EsefEnvelopeNamespaces namespaces = new();
    private bool hasInlineFact;

    internal static byte[] Compact(
        byte[] source,
        bool requireEsef = false,
        bool canonicalize = true
    )
    {
        if (
            source == null
            || source.Length == 0
            || source.Length > EsefReportEnvelope.MaximumSourceBytes
        )
            throw new InvalidDataException("Issuer report exceeds the source limit.");
        using var input = new MemoryStream(source, writable: false);
        using var reader = XmlReader.Create(
            input,
            new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Ignore,
                XmlResolver = null,
                MaxCharactersInDocument = EsefReportEnvelope.MaximumSourceBytes,
                IgnoreComments = true,
            }
        );
        using var output = new LimitedMemoryStream(EsefReportEnvelope.MaximumEnvelopeBytes);
        using (
            var writer = XmlWriter.Create(
                output,
                new XmlWriterSettings { Encoding = new UTF8Encoding(false), CloseOutput = false }
            )
        )
            new EsefEnvelopeWriter(reader, writer, canonicalize).WriteReport(requireEsef);
        return output.ToArray();
    }

    private void WriteReport(bool requireEsef)
    {
        while (reader.Read())
        {
            if (reader.Depth > MaximumDepth)
                throw new InvalidDataException("Issuer report exceeds the nesting limit.");
            if (reader.NodeType == XmlNodeType.Element)
                WriteElement(requireEsef);
            else
                WriteOtherNode();
        }
        if (requireEsef && !hasInlineFact)
            throw new InvalidDataException("Report contains no inline XBRL numeric markup.");
    }

    private void WriteOtherNode()
    {
        switch (reader.NodeType)
        {
            case XmlNodeType.EndElement:
                writer.WriteFullEndElement();
                break;
            case XmlNodeType.Text:
            case XmlNodeType.CDATA:
                writer.WriteString(reader.Value);
                break;
            case XmlNodeType.SignificantWhitespace:
            case XmlNodeType.Whitespace:
                writer.WriteWhitespace(reader.Value);
                break;
            case XmlNodeType.ProcessingInstruction:
                writer.WriteProcessingInstruction(reader.Name, reader.Value);
                break;
        }
    }

    private void WriteElement(bool requireEsef)
    {
        if (
            requireEsef
            && reader.Depth == 0
            && (reader.LocalName != "html" || reader.NamespaceURI != Xhtml)
        )
            throw new InvalidDataException("Report must be an XHTML document.");
        var inline =
            reader.NamespaceURI
            is "http://www.xbrl.org/2013/inlineXBRL"
                or "http://www.xbrl.org/2008/inlineXBRL";
        hasInlineFact |= inline && reader.LocalName == "nonFraction";
        var empty = reader.IsEmptyElement;
        var image = reader.NamespaceURI == Xhtml && reader.LocalName == "img";
        var style = reader.NamespaceURI == Xhtml && reader.LocalName == "style";
        var qnameText =
            (reader.NamespaceURI == Instance && reader.LocalName == "measure")
            || (reader.NamespaceURI == Dimensions && reader.LocalName == "explicitMember");
        var dimensionMember =
            reader.NamespaceURI == Dimensions
            && reader.LocalName is "explicitMember" or "typedMember";
        var declared = new HashSet<string>(StringComparer.Ordinal);
        writer.WriteStartElement(Prefix(), reader.LocalName, reader.NamespaceURI);
        WriteAttributes(image, inline, dimensionMember, declared);
        if (qnameText)
            WriteQNameText(empty, declared);
        else if (style && !empty)
            WriteStylesheet();
        else if (empty)
            writer.WriteEndElement();
    }

    private string Prefix() =>
        canonicalize ? namespaces.Prefix(reader.NamespaceURI, reader.Prefix) : reader.Prefix;

    private void WriteAttributes(
        bool image,
        bool inline,
        bool dimensionMember,
        HashSet<string> declared
    )
    {
        if (!reader.HasAttributes)
            return;
        while (reader.MoveToNextAttribute())
        {
            if (reader.NamespaceURI == Xmlns)
                WriteNamespace(declared);
            else if (
                image
                && reader.NamespaceURI.Length == 0
                && reader.LocalName == "src"
                && reader.Value.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase)
            )
                writer.WriteAttributeString("src", "");
            else
                writer.WriteAttributeString(
                    Prefix(),
                    reader.LocalName,
                    reader.NamespaceURI,
                    AttributeValue(inline, dimensionMember, declared)
                );
        }
        reader.MoveToElement();
    }

    private string AttributeValue(bool inline, bool dimensionMember, HashSet<string> declared)
    {
        var qname =
            (
                reader.NamespaceURI.Length == 0
                && (
                    (inline && reader.LocalName is "name" or "format")
                    || (dimensionMember && reader.LocalName == "dimension")
                )
            )
            || (
                reader.NamespaceURI == "http://www.w3.org/2001/XMLSchema-instance"
                && reader.LocalName == "type"
            );
        return canonicalize && qname
            ? namespaces.QName(reader.Value, reader, writer, declared)
            : reader.Value;
    }

    private void WriteNamespace(HashSet<string> declared)
    {
        if (!canonicalize)
        {
            writer.WriteAttributeString(
                reader.Prefix,
                reader.LocalName,
                reader.NamespaceURI,
                reader.Value
            );
            return;
        }
        var prefix = namespaces.Prefix(
            reader.Value,
            reader.Prefix == "xmlns" ? reader.LocalName : ""
        );
        if (prefix == "xml" || !declared.Add(prefix))
            return;
        if (prefix.Length == 0)
            writer.WriteAttributeString("xmlns", reader.Value);
        else
            writer.WriteAttributeString("xmlns", prefix, Xmlns, reader.Value);
    }

    private void WriteQNameText(bool empty, HashSet<string> declared)
    {
        if (empty)
            throw new InvalidDataException("An XBRL QName cannot be empty.");
        var value = reader.ReadString();
        if (reader.NodeType != XmlNodeType.EndElement)
            throw new InvalidDataException("An XBRL QName cannot contain child elements.");
        writer.WriteString(
            canonicalize ? namespaces.QName(value, reader, writer, declared) : value
        );
        writer.WriteFullEndElement();
    }

    private void WriteStylesheet()
    {
        var stylesheet = new StringBuilder();
        while (reader.Read() && reader.NodeType != XmlNodeType.EndElement)
        {
            if (reader.NodeType is XmlNodeType.Element or XmlNodeType.ProcessingInstruction)
                throw new InvalidDataException("Unexpected markup inside stylesheet.");
            if (
                reader.NodeType
                is XmlNodeType.Text
                    or XmlNodeType.CDATA
                    or XmlNodeType.Whitespace
                    or XmlNodeType.SignificantWhitespace
            )
                stylesheet.Append(reader.Value);
        }
        writer.WriteString(EsefReportContent.StripEmbeddedData(stylesheet.ToString()));
        writer.WriteFullEndElement();
    }

    private sealed class LimitedMemoryStream(int maximumBytes) : MemoryStream
    {
        public override void Write(byte[] buffer, int offset, int count)
        {
            Check(count);
            base.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            Check(buffer.Length);
            base.Write(buffer);
        }

        public override void WriteByte(byte value)
        {
            Check(1);
            base.WriteByte(value);
        }

        private void Check(int count)
        {
            if (Length + count > maximumBytes)
                throw new InvalidDataException(
                    "Issuer report exceeds the extraction envelope limit."
                );
        }
    }
}
