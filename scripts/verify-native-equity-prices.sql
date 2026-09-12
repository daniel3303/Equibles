-- Read-only full-row conservation of the migrated cohort. Native-only listings have no legacy counterpart.
BEGIN TRANSACTION ISOLATION LEVEL REPEATABLE READ READ ONLY;
DO $audit$
BEGIN
    IF EXISTS ((SELECT p."Id", p."Date", p."Open", p."High", p."Low", p."Close", p."AdjustedClose", p."Volume", p."CreationTime", p."CommonStockId" FROM "DailyStockPrice" p) EXCEPT ALL (SELECT p."Id", p."Date", p."Open", p."High", p."Low", p."Close", p."AdjustedClose", p."Volume", p."CreationTime", p."EquityIssuerId" FROM "UnattributedDailyStockPrice" p))
       OR EXISTS ((SELECT p."Id", p."Date", p."Open", p."High", p."Low", p."Close", p."AdjustedClose", p."Volume", p."CreationTime", p."EquityIssuerId" FROM "UnattributedDailyStockPrice" p) EXCEPT ALL (SELECT p."Id", p."Date", p."Open", p."High", p."Low", p."Close", p."AdjustedClose", p."Volume", p."CreationTime", p."CommonStockId" FROM "DailyStockPrice" p)) THEN
        RAISE EXCEPTION 'DailyStockPrice and UnattributedDailyStockPrice differ; native price conservation failed';
    END IF;
    IF EXISTS ((SELECT p."Id", p."Date", p."Open", p."High", p."Low", p."Close", p."AdjustedClose", p."Volume", p."CreationTime", p."CommonStockId", p."ListedTicker" FROM "ListedDailyStockPrice" p) EXCEPT ALL (SELECT p."Id", p."Date", p."Open", p."High", p."Low", p."Close", p."AdjustedClose", p."Volume", p."CreationTime", m."CommonStockId", p."SourceTicker" FROM "EquityDailyStockPrice" p JOIN "LegacyEquityListing" m ON m."EquityListingId" = p."EquityListingId"))
       OR EXISTS ((SELECT p."Id", p."Date", p."Open", p."High", p."Low", p."Close", p."AdjustedClose", p."Volume", p."CreationTime", m."CommonStockId", p."SourceTicker" FROM "EquityDailyStockPrice" p JOIN "LegacyEquityListing" m ON m."EquityListingId" = p."EquityListingId") EXCEPT ALL (SELECT p."Id", p."Date", p."Open", p."High", p."Low", p."Close", p."AdjustedClose", p."Volume", p."CreationTime", p."CommonStockId", p."ListedTicker" FROM "ListedDailyStockPrice" p)) THEN
        RAISE EXCEPTION 'ListedDailyStockPrice and EquityDailyStockPrice differ; native price conservation failed';
    END IF;
END;
$audit$;
COMMIT;
