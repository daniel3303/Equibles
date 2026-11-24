using System.Xml;

namespace Equibles.Sec.HostedService.Services;

/// <summary>
/// Confirms that a report's visual XHTML belongs to the issuer and period its JSON report proved. The index
/// record that states both addresses is what ties them together; this only refuses a body that names
/// another issuer or no context ending on the period, read forward so memory does not grow with the report.
/// </summary>
public static class EsefVisualReport
{
    private const string Instance = "http://www.xbrl.org/2003/instance";
    private const string LeiScheme = "http://standards.iso.org/iso/17442";

    /// <summary>
    /// True when every LEI the contexts name is the issuer's and a context ends on the period; false when
    /// they name another issuer or no such period; null when the body is not XHTML an XML reader accepts.
    /// </summary>
    public static bool? ConfirmsIdentity(string xhtml, string issuerLei, DateOnly periodEnd)
    {
        if (string.IsNullOrEmpty(xhtml) || string.IsNullOrEmpty(issuerLei))
            return null;
        var issuerNamed = false;
        var periodNamed = false;
        var expectedEnd = periodEnd.ToString("yyyy-MM-dd");
        try
        {
            // UTF-8 decoding keeps a byte-order mark, which an XML reader refuses; it is skipped in place
            // rather than by copying a report that can run to a few hundred million characters.
            var input = new StringReader(xhtml);
            if (xhtml[0] == '\uFEFF')
                input.Read();
            using var reader = XmlReader.Create(
                input,
                new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Ignore,
                    XmlResolver = null,
                    MaxCharactersInDocument = EsefReportEnvelope.MaximumSourceBytes,
                }
            );
            // Reading an element's content leaves the reader on the next node, so only a node that was
            // passed over asks for the next one.
            var more = reader.Read();
            while (more)
            {
                if (reader.NodeType != XmlNodeType.Element || reader.NamespaceURI != Instance)
                {
                    more = reader.Read();
                    continue;
                }
                if (reader.LocalName == "identifier" && reader.GetAttribute("scheme") == LeiScheme)
                {
                    var lei = reader.ReadElementContentAsString().Trim();
                    if (!string.Equals(lei, issuerLei, StringComparison.OrdinalIgnoreCase))
                        return false;
                    issuerNamed = true;
                }
                else if (reader.LocalName is "endDate" or "instant")
                    periodNamed |= reader.ReadElementContentAsString().Trim() == expectedEnd;
                else
                {
                    more = reader.Read();
                    continue;
                }
                more = !reader.EOF;
            }
        }
        catch (XmlException)
        {
            return null;
        }
        return issuerNamed && periodNamed;
    }
}
