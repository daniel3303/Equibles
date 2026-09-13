\set ON_ERROR_STOP on
BEGIN TRANSACTION ISOLATION LEVEL REPEATABLE READ READ ONLY;
-- Completion: every missing_issuer count is zero and all four owner FKs are validated/restrictive.
SELECT 'InstitutionalHolding' AS source, count(*) AS rows,
       count(*) FILTER (WHERE issuer."Id" IS NULL) AS missing_issuer
FROM "InstitutionalHolding" history LEFT JOIN "EquityIssuer" issuer ON issuer."Id" = history."CommonStockId"
UNION ALL
SELECT 'StockQuarterlyActivity', count(*), count(*) FILTER (WHERE issuer."Id" IS NULL)
FROM "StockQuarterlyActivity" history LEFT JOIN "EquityIssuer" issuer ON issuer."Id" = history."CommonStockId"
UNION ALL
SELECT 'StockQuarterlyActivityCombined', count(*), count(*) FILTER (WHERE issuer."Id" IS NULL)
FROM "StockQuarterlyActivityCombined" history LEFT JOIN "EquityIssuer" issuer ON issuer."Id" = history."CommonStockId"
UNION ALL
SELECT 'StockQuarterlyListingActivity', count(*), count(*) FILTER (WHERE issuer."Id" IS NULL)
FROM "StockQuarterlyListingActivity" history LEFT JOIN "EquityIssuer" issuer ON issuer."Id" = history."CommonStockId";
SELECT conrelid::regclass AS source, conname, convalidated, confdeltype = 'r' AS restricts_delete
FROM pg_constraint
WHERE confrelid = '"EquityIssuer"'::regclass
  AND conrelid IN ('"InstitutionalHolding"'::regclass, '"StockQuarterlyActivity"'::regclass,
                  '"StockQuarterlyActivityCombined"'::regclass, '"StockQuarterlyListingActivity"'::regclass)
ORDER BY source::text;
COMMIT;
