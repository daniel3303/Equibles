using System.ComponentModel.DataAnnotations;

namespace Equibles.Sec.FinancialFacts.Data.Enums;

/// <summary>
/// Which lane wrote a <see cref="Models.FinancialFact"/>. Company Facts stays authoritative for the
/// consolidated context it covers; the other lanes add what it lacks, and a Company Facts import
/// supersedes a filing-XBRL fill of a 10-Q/10-K once it carries that accession.
/// </summary>
public enum FinancialFactSource
{
    [Display(Name = "Company Facts")]
    CompanyFacts = 0,

    [Display(Name = "Filing XBRL")]
    FilingXbrl = 1,

    [Display(Name = "Exchange disclosure")]
    ExchangeDisclosure = 2,
}
