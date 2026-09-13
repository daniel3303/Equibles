BEGIN READ ONLY;
SELECT expected.table_name, progress."Completed", progress."CopiedRows",
    foreign_key.convalidated AS source_owner_validated, foreign_key.confdeltype AS delete_action
FROM (VALUES ('DailyStockPrice'), ('ListedDailyStockPrice')) expected(table_name)
LEFT JOIN "NativePriceMigrationProgress" progress ON progress."TableName" = expected.table_name
LEFT JOIN pg_constraint foreign_key ON foreign_key.conrelid = to_regclass(format('%I', expected.table_name))
    AND foreign_key.conname = 'FK_' || expected.table_name || '_EquityIssuer_CommonStockId'
    AND foreign_key.confrelid = '"EquityIssuer"'::regclass;
COMMIT;
