BEGIN READ ONLY;
SET LOCAL statement_timeout = '20min';

SELECT 'DailyShortVolume' AS observation_table, count(*) AS observations,
    count(*) FILTER (WHERE listing."Id" IS NULL) AS missing_listings,
    count(*) FILTER (WHERE observation."CommonStockId" IS NOT NULL
        AND observation."CommonStockId" <> security."EquityIssuerId") AS mismatched_original_issuers,
    count(*) FILTER (WHERE observation."CommonStockId" IS NOT NULL
        AND mapping."CommonStockId" IS DISTINCT FROM observation."CommonStockId") AS mismatched_original_listings
FROM "DailyShortVolume" observation
LEFT JOIN "EquityListing" listing ON listing."Id" = observation."EquityListingId"
LEFT JOIN "EquitySecurity" security ON security."Id" = listing."EquitySecurityId"
LEFT JOIN "LegacyEquityListing" mapping ON mapping."EquityListingId" = observation."EquityListingId";

SELECT count(*) AS duplicate_listing_dates FROM (
    SELECT "EquityListingId", "Date" FROM "DailyShortVolume"
    GROUP BY "EquityListingId", "Date" HAVING count(*) > 1
) duplicates;

SELECT conname, convalidated, confdeltype FROM pg_constraint
WHERE conname = 'FK_DailyShortVolume_EquityListing_EquityListingId'
    AND confrelid = '"EquityListing"'::regclass;



SELECT 'ShortInterest' AS observation_table, count(*) AS observations,
    count(*) FILTER (WHERE listing."Id" IS NULL) AS missing_listings,
    count(*) FILTER (WHERE observation."CommonStockId" IS NOT NULL
        AND observation."CommonStockId" <> security."EquityIssuerId") AS mismatched_original_issuers,
    count(*) FILTER (WHERE observation."CommonStockId" IS NOT NULL
        AND mapping."CommonStockId" IS DISTINCT FROM observation."CommonStockId") AS mismatched_original_listings
FROM "ShortInterest" observation
LEFT JOIN "EquityListing" listing ON listing."Id" = observation."EquityListingId"
LEFT JOIN "EquitySecurity" security ON security."Id" = listing."EquitySecurityId"
LEFT JOIN "LegacyEquityListing" mapping ON mapping."EquityListingId" = observation."EquityListingId";

SELECT count(*) AS duplicate_listing_dates FROM (
    SELECT "EquityListingId", "SettlementDate" FROM "ShortInterest"
    GROUP BY "EquityListingId", "SettlementDate" HAVING count(*) > 1
) duplicates;

SELECT conname, convalidated, confdeltype FROM pg_constraint
WHERE conname = 'FK_ShortInterest_EquityListing_EquityListingId'
    AND confrelid = '"EquityListing"'::regclass;



SELECT 'OffExchangeVolume' AS observation_table, count(*) AS observations,
    count(*) FILTER (WHERE listing."Id" IS NULL) AS missing_listings,
    count(*) FILTER (WHERE observation."CommonStockId" IS NOT NULL
        AND observation."CommonStockId" <> security."EquityIssuerId") AS mismatched_original_issuers,
    count(*) FILTER (WHERE observation."CommonStockId" IS NOT NULL
        AND mapping."CommonStockId" IS DISTINCT FROM observation."CommonStockId") AS mismatched_original_listings
FROM "OffExchangeVolume" observation
LEFT JOIN "EquityListing" listing ON listing."Id" = observation."EquityListingId"
LEFT JOIN "EquitySecurity" security ON security."Id" = listing."EquitySecurityId"
LEFT JOIN "LegacyEquityListing" mapping ON mapping."EquityListingId" = observation."EquityListingId";

SELECT count(*) AS duplicate_listing_dates FROM (
    SELECT "EquityListingId", "WeekStartDate" FROM "OffExchangeVolume"
    GROUP BY "EquityListingId", "WeekStartDate" HAVING count(*) > 1
) duplicates;

SELECT conname, convalidated, confdeltype FROM pg_constraint
WHERE conname = 'FK_OffExchangeVolume_EquityListing_EquityListingId'
    AND confrelid = '"EquityListing"'::regclass;


COMMIT;
