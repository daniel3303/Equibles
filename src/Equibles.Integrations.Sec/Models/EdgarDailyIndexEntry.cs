namespace Equibles.Integrations.Sec.Models;

/// <summary>
/// One row of SEC EDGAR's daily form index
/// (<c>/Archives/edgar/daily-index/{year}/QTR{q}/form.{yyyyMMdd}.idx</c>).
/// This is the filing firehose: every submission accepted by EDGAR on a given
/// day, regardless of filer, so it does not require a known CIK list.
/// </summary>
public class EdgarDailyIndexEntry
{
    public string FormType { get; set; }
    public string CompanyName { get; set; }
    public string Cik { get; set; }

    /// <summary>
    /// The index day for a real index row: the daily index writes its date column as <c>yyyyMMdd</c>, which this
    /// field never read, and the 13F, 13D/G and N-PORT lanes walk their frontiers on it.
    /// </summary>
    public DateOnly DateFiled { get; set; }

    /// <summary>
    /// The filing date the row itself states. EDGAR lists a filing again on a later day when it re-disseminates
    /// it, so this can be years before the index day; null when the column is unreadable.
    /// </summary>
    public DateOnly? StatedFilingDate { get; set; }

    /// <summary>Accession number with dashes, e.g. <c>0000950123-26-006477</c>.</summary>
    public string AccessionNumber { get; set; }
}
