-- Read-only completion checks: zero missing owners, one validated restrictive FK.
SELECT count(*) AS missing_issuer_owners
FROM "FundSeries" f LEFT JOIN "EquityIssuer" i ON i."Id" = f."CommonStockId"
WHERE f."CommonStockId" IS NOT NULL AND i."Id" IS NULL;

SELECT conname, convalidated, confdeltype = 'r' AS restricts_deletion
FROM pg_constraint
WHERE conrelid = '"FundSeries"'::regclass
  AND confrelid = '"EquityIssuer"'::regclass AND contype = 'f';
