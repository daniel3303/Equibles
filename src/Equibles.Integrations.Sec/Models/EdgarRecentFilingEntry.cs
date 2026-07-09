namespace Equibles.Integrations.Sec.Models;

/// <summary>
/// One entry of SEC EDGAR's "Latest Filings" ATOM feed
/// (<c>/cgi-bin/browse-edgar?action=getcurrent&amp;output=atom</c>) — the
/// real-time dissemination stream. A single submission produces one entry per
/// associated entity (filer, subject company, reporting person), each carrying
/// that entity's own CIK, so ownership forms surface both the person and the
/// issuer. The feed is a rolling window of the most recent entries only; a
/// burst larger than the window scrolls older entries out unseen, so it can
/// never be the sole discovery source.
/// </summary>
public class EdgarRecentFilingEntry
{
    /// <summary>Zero-padded 10-digit CIK of the entity this entry is about.</summary>
    public string Cik { get; set; }

    public string FormType { get; set; }

    /// <summary>Accession number with dashes, e.g. <c>0001995137-26-000012</c>.</summary>
    public string AccessionNumber { get; set; }

    public string CompanyName { get; set; }

    public DateTimeOffset? Updated { get; set; }
}
