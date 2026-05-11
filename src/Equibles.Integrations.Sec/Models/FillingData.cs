namespace Equibles.Integrations.Sec.Models;

public class FilingData {
    /// <summary>
    /// CIK of the <em>filer</em> of this document, which may be a subsidiary that shares
    /// its parent's public ticker. Use this for retrieving the document content from
    /// SEC's archive. For stock attribution, use the <c>CommonStock</c> instance the
    /// filing is being processed under — that may be the parent even when this CIK is
    /// the subsidiary.
    /// </summary>
    public string Cik { get; set; }
    public string AccessionNumber { get; set; }
    public DateOnly FilingDate { get; set; }
    public DateOnly ReportDate { get; set; }
    public string Form { get; set; }
    public string PrimaryDocument { get; set; }
    public string Description { get; set; }
    public string DocumentUrl { get; set; }
}