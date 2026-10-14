namespace Equibles.CommonStocks.BusinessLogic.Merge;

/// <summary>Merge rules for the open-source tables that hold an issuer key.</summary>
public static class CommonEquityReferenceRules
{
    public static IReadOnlyList<EquityReferenceRule> All { get; } =
    [
        // Facts left after the withdrawn sources belong to a governing source the survivor lacks.
        new("FinancialFact") { RefuseAnyRow = true },
        // Issuer-scoped observations of another venue would join the survivor's price series.
        new("UnattributedDailyStockPrice") { RefuseAnyRow = true },
        // Listing-bound actions must be re-inserted once their security belongs to the survivor.
        new("CashDividend") { Reinsert = true, OnCollision = EquityReferenceCollision.KeepSurvivor },
        new("StockSplit") { Reinsert = true, OnCollision = EquityReferenceCollision.KeepSurvivor },
        // Per-issuer state and evidence the survivor already keeps for the same company.
        new("EquityIssuerPresentation") { OnCollision = EquityReferenceCollision.KeepSurvivor },
        new("CompanyFilingSyncState") { OnCollision = EquityReferenceCollision.KeepSurvivor },
        new("FinancialFactsSyncStatus") { OnCollision = EquityReferenceCollision.KeepSurvivor },
        new("EquityIssuerTickerEvidence") { OnCollision = EquityReferenceCollision.KeepSurvivor },
        new("HoldingLabelConvergenceState") { OnCollision = EquityReferenceCollision.KeepSurvivor },
        new("HoldingsCusipRescan"),
        // A collision here means two records disagree about the same filing or listing; an operator decides.
        new("EquityListingRetirementEvidence"),
        new("IssuerSecurityRegistration"),
        new("InstitutionalHolding"),
        new("StockQuarterlyActivity"),
        new("StockQuarterlyActivityCombined"),
        new("StockQuarterlyListingActivity"),
    ];
}
