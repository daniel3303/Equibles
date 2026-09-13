-- Exact row equivalence during the finite compatibility window; no database writes.
BEGIN TRANSACTION ISOLATION LEVEL REPEATABLE READ READ ONLY;
DO $verify$ BEGIN
    IF EXISTS (SELECT 1 FROM "CommonStockCusipAlias" WHERE "CommonStockId" IS DISTINCT FROM "EquityIssuerId") THEN
        RAISE EXCEPTION 'Owner mismatch: CommonStockCusipAlias'; END IF;
    IF EXISTS (
        (SELECT to_jsonb(row) - 'CommonStockId' FROM "CommonStockCusipAlias" row EXCEPT ALL SELECT to_jsonb(row) FROM "EquityIssuerCusipAlias" row)
        UNION ALL
        (SELECT to_jsonb(row) FROM "EquityIssuerCusipAlias" row EXCEPT ALL SELECT to_jsonb(row) - 'CommonStockId' FROM "CommonStockCusipAlias" row)
    ) THEN RAISE EXCEPTION 'Evidence mismatch: CommonStockCusipAlias / EquityIssuerCusipAlias'; END IF;
    IF EXISTS (SELECT 1 FROM "CommonStockTickerAlias" WHERE "CommonStockId" IS DISTINCT FROM "EquityIssuerId") THEN
        RAISE EXCEPTION 'Owner mismatch: CommonStockTickerAlias'; END IF;
    IF EXISTS (
        (SELECT to_jsonb(row) - 'CommonStockId' FROM "CommonStockTickerAlias" row EXCEPT ALL SELECT to_jsonb(row) FROM "EquityIssuerTickerAlias" row)
        UNION ALL
        (SELECT to_jsonb(row) FROM "EquityIssuerTickerAlias" row EXCEPT ALL SELECT to_jsonb(row) - 'CommonStockId' FROM "CommonStockTickerAlias" row)
    ) THEN RAISE EXCEPTION 'Evidence mismatch: CommonStockTickerAlias / EquityIssuerTickerAlias'; END IF;
    IF EXISTS (SELECT 1 FROM "CommonStockTickerEvidence" WHERE "CommonStockId" IS DISTINCT FROM "EquityIssuerId") THEN
        RAISE EXCEPTION 'Owner mismatch: CommonStockTickerEvidence'; END IF;
    IF EXISTS (
        (SELECT to_jsonb(row) - 'CommonStockId' FROM "CommonStockTickerEvidence" row EXCEPT ALL SELECT to_jsonb(row) FROM "EquityIssuerTickerEvidence" row)
        UNION ALL
        (SELECT to_jsonb(row) FROM "EquityIssuerTickerEvidence" row EXCEPT ALL SELECT to_jsonb(row) - 'CommonStockId' FROM "CommonStockTickerEvidence" row)
    ) THEN RAISE EXCEPTION 'Evidence mismatch: CommonStockTickerEvidence / EquityIssuerTickerEvidence'; END IF;
    IF EXISTS (SELECT 1 FROM "CommonStockListedCusip" WHERE "CommonStockId" IS DISTINCT FROM "EquityIssuerId") THEN
        RAISE EXCEPTION 'Owner mismatch: CommonStockListedCusip'; END IF;
    IF EXISTS (
        (SELECT to_jsonb(row) - 'CommonStockId' FROM "CommonStockListedCusip" row EXCEPT ALL SELECT to_jsonb(row) FROM "EquityListingCusipEvidence" row)
        UNION ALL
        (SELECT to_jsonb(row) FROM "EquityListingCusipEvidence" row EXCEPT ALL SELECT to_jsonb(row) - 'CommonStockId' FROM "CommonStockListedCusip" row)
    ) THEN RAISE EXCEPTION 'Evidence mismatch: CommonStockListedCusip / EquityListingCusipEvidence'; END IF;
    IF EXISTS (SELECT 1 FROM "CommonStockDelistedListing" WHERE "CommonStockId" IS DISTINCT FROM "EquityIssuerId") THEN
        RAISE EXCEPTION 'Owner mismatch: CommonStockDelistedListing'; END IF;
    IF EXISTS (
        (SELECT to_jsonb(row) - 'CommonStockId' FROM "CommonStockDelistedListing" row EXCEPT ALL SELECT to_jsonb(row) FROM "EquityListingRetirementEvidence" row)
        UNION ALL
        (SELECT to_jsonb(row) FROM "EquityListingRetirementEvidence" row EXCEPT ALL SELECT to_jsonb(row) - 'CommonStockId' FROM "CommonStockDelistedListing" row)
    ) THEN RAISE EXCEPTION 'Evidence mismatch: CommonStockDelistedListing / EquityListingRetirementEvidence'; END IF;
    IF EXISTS (SELECT 1 FROM "ListedSecurity" WHERE "CommonStockId" IS DISTINCT FROM "EquityIssuerId") THEN
        RAISE EXCEPTION 'Owner mismatch: ListedSecurity'; END IF;
    IF EXISTS (
        (SELECT to_jsonb(row) - 'CommonStockId' FROM "ListedSecurity" row EXCEPT ALL SELECT to_jsonb(row) FROM "IssuerSecurityRegistration" row)
        UNION ALL
        (SELECT to_jsonb(row) FROM "IssuerSecurityRegistration" row EXCEPT ALL SELECT to_jsonb(row) - 'CommonStockId' FROM "ListedSecurity" row)
    ) THEN RAISE EXCEPTION 'Evidence mismatch: ListedSecurity / IssuerSecurityRegistration'; END IF;
END $verify$;
COMMIT;
