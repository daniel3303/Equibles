using Equibles.Sec.Data.Models;

namespace Equibles.Sec.BusinessLogic.Search;

/// <summary>
/// Optional deployment seam: builds a public URL for one document excerpt so search
/// renderers can attach a "view this passage" link. The framework registers no
/// implementation — a deployment that has a public document viewer registers its own,
/// and everything degrades to link-less rendering when none is registered (the
/// consumers take this as an optional constructor dependency).
/// </summary>
public interface IDocumentExcerptLinkBuilder
{
    /// <summary>
    /// Absolute URL for an excerpt of <paramref name="document"/>, or null when no link
    /// applies (e.g. the document's stock has no public page). Line numbers are 1-based
    /// positions in the document's normalized text content — approximate anchors the
    /// viewer may refine with <paramref name="excerptText"/>, which is the excerpt's raw
    /// text (implementations truncate and encode it themselves).
    /// </summary>
    string BuildExcerptUrl(Document document, int fromLine, int toLine, string excerptText);
}
