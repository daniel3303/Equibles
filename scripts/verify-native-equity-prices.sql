-- Compare every original row, including retained rows whose old directory owner was removed.
-- Native histories without either counterpart require the immutable pre-cutover export.
BEGIN TRANSACTION ISOLATION LEVEL REPEATABLE READ READ ONLY;
DO $audit$
BEGIN
    IF to_regclass('"EquityListingTickerAlias"') IS NULL THEN
        IF EXISTS (SELECT 1 FROM "EquityDailyStockPrice" p
            JOIN "LegacyEquityListing" mapping ON mapping."EquityListingId" = p."EquityListingId"
            WHERE p."SourceTicker" IS DISTINCT FROM mapping."ListedTicker") THEN
            RAISE EXCEPTION 'Initial native price source differs from its original series key';
        END IF;
    ELSE
    IF EXISTS (
        SELECT 1 FROM "EquityDailyStockPrice" p
        JOIN "LegacyEquityListing" mapping ON mapping."EquityListingId" = p."EquityListingId"
        JOIN "EquityListing" listing ON listing."Id" = p."EquityListingId"
        WHERE p."SourceTicker" IS DISTINCT FROM mapping."ListedTicker"
          AND p."SourceTicker" IS DISTINCT FROM listing."Ticker"
          AND NOT EXISTS (SELECT 1 FROM "EquityListingTickerAlias" alias
              WHERE alias."EquityListingId" = p."EquityListingId" AND alias."Ticker" = p."SourceTicker")
    ) THEN
        RAISE EXCEPTION 'Native price source symbol has no retained listing identity';
    END IF;
    END IF;
    IF EXISTS ((SELECT p."Id", p."Date", p."Open", p."High", p."Low", p."Close", p."AdjustedClose", p."Volume", p."CreationTime", p."CommonStockId" FROM "DailyStockPrice" p) EXCEPT ALL (SELECT p."Id", p."Date", p."Open", p."High", p."Low", p."Close", p."AdjustedClose", p."Volume", p."CreationTime", p."EquityIssuerId" FROM "UnattributedDailyStockPrice" p WHERE EXISTS (SELECT 1 FROM "CommonStock" owner WHERE owner."Id" = p."EquityIssuerId") OR EXISTS (SELECT 1 FROM "DailyStockPrice" original WHERE original."Id" = p."Id")))
       OR EXISTS ((SELECT p."Id", p."Date", p."Open", p."High", p."Low", p."Close", p."AdjustedClose", p."Volume", p."CreationTime", p."EquityIssuerId" FROM "UnattributedDailyStockPrice" p WHERE EXISTS (SELECT 1 FROM "CommonStock" owner WHERE owner."Id" = p."EquityIssuerId") OR EXISTS (SELECT 1 FROM "DailyStockPrice" original WHERE original."Id" = p."Id")) EXCEPT ALL (SELECT p."Id", p."Date", p."Open", p."High", p."Low", p."Close", p."AdjustedClose", p."Volume", p."CreationTime", p."CommonStockId" FROM "DailyStockPrice" p)) THEN
        RAISE EXCEPTION 'DailyStockPrice and UnattributedDailyStockPrice differ; native price conservation failed';
    END IF;
    IF EXISTS ((SELECT p."Id", p."Date", p."Open", p."High", p."Low", p."Close", p."AdjustedClose", p."Volume", p."CreationTime", p."CommonStockId", p."ListedTicker" FROM "ListedDailyStockPrice" p) EXCEPT ALL (SELECT p."Id", p."Date", p."Open", p."High", p."Low", p."Close", p."AdjustedClose", p."Volume", p."CreationTime", m."CommonStockId", m."ListedTicker" FROM "EquityDailyStockPrice" p JOIN "LegacyEquityListing" m ON m."EquityListingId" = p."EquityListingId" WHERE EXISTS (SELECT 1 FROM "CommonStock" owner WHERE owner."Id" = m."CommonStockId") OR EXISTS (SELECT 1 FROM "ListedDailyStockPrice" original WHERE original."Id" = p."Id")))
       OR EXISTS ((SELECT p."Id", p."Date", p."Open", p."High", p."Low", p."Close", p."AdjustedClose", p."Volume", p."CreationTime", m."CommonStockId", m."ListedTicker" FROM "EquityDailyStockPrice" p JOIN "LegacyEquityListing" m ON m."EquityListingId" = p."EquityListingId" WHERE EXISTS (SELECT 1 FROM "CommonStock" owner WHERE owner."Id" = m."CommonStockId") OR EXISTS (SELECT 1 FROM "ListedDailyStockPrice" original WHERE original."Id" = p."Id")) EXCEPT ALL (SELECT p."Id", p."Date", p."Open", p."High", p."Low", p."Close", p."AdjustedClose", p."Volume", p."CreationTime", p."CommonStockId", p."ListedTicker" FROM "ListedDailyStockPrice" p)) THEN
        RAISE EXCEPTION 'ListedDailyStockPrice and EquityDailyStockPrice differ; native price conservation failed';
    END IF;
END;
$audit$;
SELECT 'retained_exact_without_legacy_owner' AS cohort, count(*) AS rows
FROM "EquityDailyStockPrice" p
JOIN "LegacyEquityListing" mapping ON mapping."EquityListingId" = p."EquityListingId"
WHERE NOT EXISTS (SELECT 1 FROM "CommonStock" owner WHERE owner."Id" = mapping."CommonStockId")
UNION ALL
SELECT 'retained_unattributed_without_legacy_owner', count(*)
FROM "UnattributedDailyStockPrice" p
WHERE NOT EXISTS (SELECT 1 FROM "CommonStock" owner WHERE owner."Id" = p."EquityIssuerId");
COMMIT;
