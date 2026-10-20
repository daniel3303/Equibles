namespace Equibles.Holdings.HostedService.Models;

// Ad hoc rows of the fund-ownership drain's SQL. Public and unsealed because the lazy-loading
// proxy convention validates every type a SqlQuery materialises.

/// <summary>A series' latest Form NPORT-P inside one quarter.</summary>
public class FundOwnershipFilingRow
{
    public Guid Id { get; set; }

    /// <summary>The series id upper-cased, or null for a registrant's single id-less fund.</summary>
    public string SeriesId { get; set; }

    public Guid? EquityIssuerId { get; set; }
    public string RegistrantCik { get; set; }
    public DateOnly ReportPeriodDate { get; set; }
}

/// <summary>
/// Long share positions summed per fund-type bucket, linked 13F filer, security CUSIP and
/// portfolio date across the chosen reports.
/// </summary>
public class FundOwnershipLineRow
{
    public int Bucket { get; set; }
    public Guid? HolderId { get; set; }
    public string Cusip { get; set; }
    public DateOnly ReportPeriodDate { get; set; }
    public decimal Balance { get; set; }
    public decimal Value { get; set; }
    public int SeriesCount { get; set; }
}

/// <summary>The N-PORT filings whose portfolio date falls inside one calendar quarter.</summary>
public class FundOwnershipNportInputRow
{
    public DateOnly Quarter { get; set; }
    public int Filings { get; set; }
    public DateTime Latest { get; set; }
    public long ParserVersions { get; set; }
}

/// <summary>The N-CEN fund reports whose period ends inside one calendar quarter.</summary>
public class FundOwnershipNCenInputRow
{
    public DateOnly Quarter { get; set; }
    public int Reports { get; set; }
    public DateOnly LatestFiling { get; set; }
}
