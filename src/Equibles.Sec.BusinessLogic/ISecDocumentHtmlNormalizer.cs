namespace Equibles.Sec.BusinessLogic;

public interface ISecDocumentHtmlNormalizer
{
    public string Normalize(string html);

    /// <summary>
    /// Runs the same normalization steps over a bare HTML document, one that is not wrapped in an EDGAR
    /// submission envelope. <see cref="Normalize"/> returns nothing for such a document because its first
    /// step is to select the allowed forms out of the envelope's SGML blocks.
    /// </summary>
    public string NormalizeFragment(string html);
}
