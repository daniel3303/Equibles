using Equibles.Integrations.Sec.Models;

namespace Equibles.Integrations.Sec.Contracts;

public interface ISecEdgarClient {
    Task<List<CompanyInfo>> GetActiveCompanies();
    Task<string> GetEntityType(string cik);
    Task<List<FilingData>> GetCompanyFilings(string cik, DocumentTypeFilter? documentType = null, DateOnly? fromDate = null, DateOnly? toDate = null);
    Task<string> GetDocumentContent(string accessionNumber, string cik);
    Task<string> GetDocumentContent(FilingData filing);

    /// <summary>
    /// Downloads a file from sec.gov with rate limiting, retries, and 429 handling.
    /// Use this for any SEC download (FTD files, 13F data sets, etc.).
    /// </summary>
    Task<Stream> DownloadStream(string url);
}