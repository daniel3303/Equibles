namespace Equibles.Sec.FinancialFacts.HostedService.Services;

/// <summary>
/// One backfill pass: the sets it added, and the keys no fact could state in full, counted once
/// per issuer batch that holds them.
/// </summary>
public record FinancialDimensionSetBackfillPass(int Added, int Unresolved);
