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
        // Listing-bound actions are re-inserted once their security belongs to the survivor; issuer-level
        // actions would join the survivor's share-count restatement, so they refuse.
        new("CashDividend") { Reinsert = true, RefuseRowsWithout = "EquityListingId" },
        // A share-count verdict was judged against the retired issuer's counts, so it is judged again.
        new("StockSplit")
        {
            Reinsert = true,
            RefuseRowsWithout = "EquityListingId",
            ReinsertResets =
            [
                "ShareCountEvidence",
                "ShareCountEvidenceAsOf",
                "ShareCountEvidenceTime",
            ],
        },
        // Per-issuer state and evidence the survivor already keeps for the same company.
        new("EquityIssuerPresentation") { OnCollision = EquityReferenceCollision.KeepSurvivor },
        new("CompanyFilingSyncState") { OnCollision = EquityReferenceCollision.KeepSurvivor },
        new("FinancialFactsSyncStatus") { OnCollision = EquityReferenceCollision.KeepSurvivor },
        new("EquityIssuerTickerEvidence") { OnCollision = EquityReferenceCollision.KeepSurvivor },
        new("HoldingLabelConvergenceState") { OnCollision = EquityReferenceCollision.KeepSurvivor },
        // A presence mask is derived from holdings; the next rebuild of each quarter restores a dropped bit.
        new("HolderStockPresence") { OnCollision = EquityReferenceCollision.KeepSurvivor },
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
