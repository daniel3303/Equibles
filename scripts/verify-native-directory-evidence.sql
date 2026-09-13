\set ON_ERROR_STOP on
BEGIN TRANSACTION ISOLATION LEVEL REPEATABLE READ READ ONLY;
-- Completion: zero missing issuers and four validated restrictive issuer foreign keys.
SELECT 'CommonStockCusipAlias' AS source, count(*) AS rows,
       count(*) FILTER (WHERE issuer."Id" IS NULL) AS missing_issuer
FROM "CommonStockCusipAlias" evidence LEFT JOIN "EquityIssuer" issuer ON issuer."Id" = evidence."CommonStockId"
UNION ALL
SELECT 'CommonStockListedCusip' AS source, count(*) AS rows,
       count(*) FILTER (WHERE issuer."Id" IS NULL) AS missing_issuer
FROM "CommonStockListedCusip" evidence LEFT JOIN "EquityIssuer" issuer ON issuer."Id" = evidence."CommonStockId"
UNION ALL
SELECT 'CommonStockTickerAlias' AS source, count(*) AS rows,
       count(*) FILTER (WHERE issuer."Id" IS NULL) AS missing_issuer
FROM "CommonStockTickerAlias" evidence LEFT JOIN "EquityIssuer" issuer ON issuer."Id" = evidence."CommonStockId"
UNION ALL
SELECT 'CommonStockDelistedListing' AS source, count(*) AS rows,
       count(*) FILTER (WHERE issuer."Id" IS NULL) AS missing_issuer
FROM "CommonStockDelistedListing" evidence LEFT JOIN "EquityIssuer" issuer ON issuer."Id" = evidence."CommonStockId";
SELECT conrelid::regclass AS source, conname, convalidated, confdeltype = 'r' AS restricts_delete
FROM pg_constraint WHERE confrelid = '"EquityIssuer"'::regclass
  AND conrelid IN ('"CommonStockCusipAlias"'::regclass, '"CommonStockListedCusip"'::regclass, '"CommonStockTickerAlias"'::regclass, '"CommonStockDelistedListing"'::regclass)
ORDER BY source::text;
COMMIT;
