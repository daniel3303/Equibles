-- Read-only completion checks. All missing owner counts must be zero.
SELECT 'InsiderTransaction' AS source, count(*) AS missing_owners
FROM "InsiderTransaction" f LEFT JOIN "EquityIssuer" i ON i."Id"=f."CommonStockId" WHERE i."Id" IS NULL
UNION ALL SELECT 'Form144Filing', count(*)
FROM "Form144Filing" f LEFT JOIN "EquityIssuer" i ON i."Id"=f."CommonStockId" WHERE i."Id" IS NULL
UNION ALL SELECT 'GovernmentContract', count(*)
FROM "GovernmentContract" f LEFT JOIN "EquityIssuer" i ON i."Id"=f."CommonStockId" WHERE i."Id" IS NULL
UNION ALL SELECT 'FdaCatalyst', count(*)
FROM "FdaCatalyst" f LEFT JOIN "EquityIssuer" i ON i."Id"=f."CommonStockId"
WHERE f."CommonStockId" IS NOT NULL AND i."Id" IS NULL;

-- Four validated restrictive constraints are required.
SELECT conname, convalidated, confdeltype = 'r' AS restricts_deletion
FROM pg_constraint WHERE confrelid='"EquityIssuer"'::regclass AND contype='f'
AND conrelid IN ('"InsiderTransaction"'::regclass,'"Form144Filing"'::regclass,
'"GovernmentContract"'::regclass,'"FdaCatalyst"'::regclass);
