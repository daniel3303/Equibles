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
    public DateOnly DateFiled { get; set; }

    /// <summary>Accession number with dashes, e.g. <c>0000950123-26-006477</c>.</summary>
    public string AccessionNumber { get; set; }
}
