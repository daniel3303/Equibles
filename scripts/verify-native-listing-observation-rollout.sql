BEGIN READ ONLY;
SELECT expected.table_name, progress."Completed", progress."UpdatedRows",
    attribute.attnotnull AS listing_required,
    indexes.indisvalid AS index_valid, indexes.indisready AS index_ready,
    foreign_key.convalidated AS foreign_key_validated, foreign_key.confdeltype AS delete_action
FROM (VALUES
    ('FailToDeliver', 'SettlementDate'),
    ('DailyShortVolume', 'Date'),
    ('ShortInterest', 'SettlementDate'),
    ('OffExchangeVolume', 'WeekStartDate')
) expected(table_name, date_column)
LEFT JOIN "NativeListingObservationMigrationProgress" progress ON progress."TableName" = expected.table_name
LEFT JOIN pg_attribute attribute ON attribute.attrelid = to_regclass(format('%I', expected.table_name))
    AND attribute.attname = 'EquityListingId' AND NOT attribute.attisdropped
LEFT JOIN pg_index indexes ON indexes.indexrelid = to_regclass(format('%I', 'IX_' || expected.table_name || '_EquityListingId_' || expected.date_column))
LEFT JOIN pg_constraint foreign_key ON foreign_key.conrelid = to_regclass(format('%I', expected.table_name))
    AND foreign_key.conname = 'FK_' || expected.table_name || '_EquityListing_EquityListingId'
    AND foreign_key.confrelid = '"EquityListing"'::regclass;
COMMIT;
