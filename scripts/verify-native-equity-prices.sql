-- Read-only, full-row conservation audit before enabling native-only writers.
BEGIN TRANSACTION ISOLATION LEVEL REPEATABLE READ READ ONLY;
DO $audit$
BEGIN
    IF EXISTS ((SELECT p."Id", p."Date", p."Open", p."High", p."Low", p."Close", p."AdjustedClose", p."Volume", p."CreationTime", p."CommonStockId" FROM "DailyStockPrice" p) EXCEPT ALL (SELECT p."Id", p."Date", p."Open", p."High", p."Low", p."Close", p."AdjustedClose", p."Volume", p."CreationTime", i."CommonStockId" FROM "UnattributedDailyStockPrice" p JOIN "EquityIssuer" i ON i."Id" = p."EquityIssuerId"))
       OR EXISTS ((SELECT p."Id", p."Date", p."Open", p."High", p."Low", p."Close", p."AdjustedClose", p."Volume", p."CreationTime", i."CommonStockId" FROM "UnattributedDailyStockPrice" p JOIN "EquityIssuer" i ON i."Id" = p."EquityIssuerId") EXCEPT ALL (SELECT p."Id", p."Date", p."Open", p."High", p."Low", p."Close", p."AdjustedClose", p."Volume", p."CreationTime", p."CommonStockId" FROM "DailyStockPrice" p)) THEN
        RAISE EXCEPTION 'DailyStockPrice and UnattributedDailyStockPrice differ; native price conservation failed';
    END IF;
    IF EXISTS ((SELECT p."Id", p."Date", p."Open", p."High", p."Low", p."Close", p."AdjustedClose", p."Volume", p."CreationTime", p."CommonStockId", p."ListedTicker" FROM "ListedDailyStockPrice" p) EXCEPT ALL (SELECT p."Id", p."Date", p."Open", p."High", p."Low", p."Close", p."AdjustedClose", p."Volume", p."CreationTime", i."CommonStockId", p."SourceTicker" FROM "EquityDailyStockPrice" p JOIN "EquityListing" l ON l."Id" = p."EquityListingId" JOIN "EquitySecurity" s ON s."Id" = l."EquitySecurityId" JOIN "EquityIssuer" i ON i."Id" = s."EquityIssuerId"))
       OR EXISTS ((SELECT p."Id", p."Date", p."Open", p."High", p."Low", p."Close", p."AdjustedClose", p."Volume", p."CreationTime", i."CommonStockId", p."SourceTicker" FROM "EquityDailyStockPrice" p JOIN "EquityListing" l ON l."Id" = p."EquityListingId" JOIN "EquitySecurity" s ON s."Id" = l."EquitySecurityId" JOIN "EquityIssuer" i ON i."Id" = s."EquityIssuerId") EXCEPT ALL (SELECT p."Id", p."Date", p."Open", p."High", p."Low", p."Close", p."AdjustedClose", p."Volume", p."CreationTime", p."CommonStockId", p."ListedTicker" FROM "ListedDailyStockPrice" p)) THEN
        RAISE EXCEPTION 'ListedDailyStockPrice and EquityDailyStockPrice differ; native price conservation failed';
    END IF;
END;
$audit$;
COMMIT;
