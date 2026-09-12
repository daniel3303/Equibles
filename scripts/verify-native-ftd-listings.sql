BEGIN READ ONLY;
SET LOCAL statement_timeout = '10min';

SELECT count(*) AS observations,
    count(*) FILTER (WHERE listing."Id" IS NULL) AS missing_listings,
    count(*) FILTER (WHERE observation."CommonStockId" IS NOT NULL
        AND observation."CommonStockId" <> security."EquityIssuerId") AS mismatched_original_issuers,
    count(*) FILTER (WHERE observation."CommonStockId" IS NOT NULL
        AND mapping."EquityListingId" IS DISTINCT FROM observation."EquityListingId") AS mismatched_original_listings
FROM "FailToDeliver" observation
LEFT JOIN "EquityListing" listing ON listing."Id" = observation."EquityListingId"
LEFT JOIN "EquitySecurity" security ON security."Id" = listing."EquitySecurityId"
LEFT JOIN "LegacyEquityListing" mapping ON mapping."CommonStockId" = observation."CommonStockId"
    AND mapping."ListedTicker" = observation."ListedTicker";

SELECT count(*) AS duplicate_listing_dates FROM (
    SELECT "EquityListingId", "SettlementDate" FROM "FailToDeliver"
    GROUP BY "EquityListingId", "SettlementDate" HAVING count(*) > 1
) duplicates;

SELECT conname, convalidated, confdeltype FROM pg_constraint
WHERE conname = 'FK_FailToDeliver_EquityListing_EquityListingId'
    AND confrelid = '"EquityListing"'::regclass;

COMMIT;
