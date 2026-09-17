namespace Equibles.Integrations.XbrlFilings.Models;

// One row of the filing index: who filed, for which period, under which regime, and the exact addresses the
// host states for the report and its renderings. Every address is taken verbatim; none is ever composed.
public sealed record XbrlFiling
{
    public string EntityIdentifier { get; init; }

    public string EntityName { get; init; }

    public string Regime { get; init; }

    public string CountryCode { get; init; }

    public DateOnly? PeriodEnd { get; init; }

    public DateTime? AddedAt { get; init; }

    public int ErrorCount { get; init; }

    public string Sha256 { get; init; }

    public Uri ReportUrl { get; init; }

    public Uri PackageUrl { get; init; }

    public Uri JsonUrl { get; init; }

    public string FilingKey { get; init; }
}
