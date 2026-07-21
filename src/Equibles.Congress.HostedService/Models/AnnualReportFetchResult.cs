namespace Equibles.Congress.HostedService.Models;

/// <summary>
/// An annual-report fetch pass: the parsed reports plus the filings that were
/// fully handled (including deterministic policy skips such as scanned paper
/// or candidate reports) and may be marked as ingested once the reports are
/// committed.
/// </summary>
public class AnnualReportFetchResult
{
    public List<AnnualDisclosureReport> Reports { get; set; } = [];
    public List<ProcessedFiling> ProcessedFilings { get; set; } = [];
}
