using Equibles.Integrations.Sec.Models;
using Equibles.Integrations.Sec.Models.Responses;

namespace Equibles.Integrations.Sec.Contracts;

public interface ISecEdgarClient
{
    Task<List<CompanyInfo>> GetActiveCompanies();
    Task<string> GetEntityType(string cik);
    Task<CompanyMetadata> GetCompanyMetadata(string cik);
    Task<List<FilingData>> GetCompanyFilings(
        string cik,
        DocumentTypeFilter? documentType = null,
        DateOnly? fromDate = null,
        DateOnly? toDate = null
    );
    Task<string> GetDocumentContent(string accessionNumber, string cik);
    Task<string> GetDocumentContent(FilingData filing);

    /// <summary>
    /// Fetches SEC's pre-parsed, standardized XBRL facts for a company
    /// (Company Facts API). Returns null when the company has no XBRL facts
    /// (404) or the payload cannot be parsed.
    /// </summary>
    Task<CompanyFactsResponse> GetCompanyFacts(string cik);

    /// <summary>
    /// Fetches a single artifact (e.g. an attached PDF) inside a filing by filename,
    /// using the per-file URL pattern /Archives/edgar/data/{cik}/{accession-no-dashes}/{filename}.
    /// </summary>
    Task<byte[]> GetDocumentFileBytes(
        string cik,
        string accessionNumber,
        string filename,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Downloads a file from sec.gov with rate limiting, retries, and 429 handling.
    /// Use this for any SEC download (FTD files, 13F data sets, etc.).
    /// </summary>
    Task<Stream> DownloadStream(string url);

    /// <summary>
    /// Fetches SEC EDGAR's daily form index for a single calendar day — every
    /// submission accepted that day, by any filer. Returns an empty list for
    /// non-publishing days (weekends/holidays → 404), so callers can sweep a
    /// date range without special-casing.
    /// </summary>
    Task<List<EdgarDailyIndexEntry>> GetDailyIndex(
        DateOnly date,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Lists the artifact file names inside a single filing via its
    /// <c>index.json</c>. Used to locate <c>primary_doc.xml</c> and the
    /// information-table XML of a 13F-HR submission.
    /// </summary>
    Task<List<string>> GetFilingArtifactNames(
        string cik,
        string accessionNumber,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Returns the most recent <c>reportDate</c> for a given form type from the
    /// SEC submissions feed's <c>filings.recent</c> section. Uses the cached
    /// submissions payload when available (zero extra SEC requests after
    /// <see cref="GetCompanyMetadata"/>). Only examines recent filings — no
    /// archive fetching. Returns null when no filing of the given type exists.
    /// </summary>
    Task<DateOnly?> GetMostRecentReportDate(string cik, DocumentTypeFilter formType);
}
