using Equibles.Sec.Data.Models;

namespace Equibles.Sec.BusinessLogic.Search.Models;

/// <summary>
/// Lightweight data transfer object containing essential information about SEC documents.
/// Used for listing documents without loading heavy entity relationships like chunks and file content.
/// </summary>
public class SecDocumentInfo
{
    /// <summary>
    /// Unique identifier of the document in the database.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Stock ticker symbol of the company that filed the document (e.g., "AAPL", "MSFT").
    /// </summary>
    public string Ticker { get; set; }

    /// <summary>
    /// Full legal name of the company that filed the document.
    /// </summary>
    public string CompanyName { get; set; }

    /// <summary>
    /// Type of SEC document (10-K, 10-Q, 8-K, etc.).
    /// </summary>
    public DocumentType DocumentType { get; set; }

    /// <summary>
    /// Date when the document was officially filed with the SEC.
    /// </summary>
    public DateOnly ReportingDate { get; set; }

    /// <summary>
    /// The reporting period end date that the document covers (e.g., fiscal quarter or year end),
    /// or null when the filing states none.
    /// </summary>
    public DateOnly? ReportingForDate { get; set; }

    /// <summary>
    /// Total number of lines in the document content.
    /// </summary>
    public int LineCount { get; set; }

    /// <summary>
    /// Comma-separated SEC item numbers reported by the filing, such as 2.02 and 9.01.
    /// </summary>
    public string Items { get; set; }

    /// <summary>
    /// EDGAR accession number; null for documents that are not EDGAR filings, whose key in that
    /// column is not an accession.
    /// </summary>
    public string AccessionNumber { get; set; }

    /// <summary>
    /// When EDGAR accepted the filing (UTC); null for filings ingested before capture began.
    /// </summary>
    public DateTime? AcceptedAt { get; set; }

    /// <summary>
    /// When the document was stored here (UTC).
    /// </summary>
    public DateTime IngestedAt { get; set; }

    /// <summary>
    /// A short plain-language summary of what the document says, or null when the deployment
    /// provides none for it (see <see cref="IDocumentSummaryReader"/>).
    /// </summary>
    public string Summary { get; set; }
}
