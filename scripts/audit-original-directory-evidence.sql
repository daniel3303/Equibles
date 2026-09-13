-- Transaction-neutral audit body: caller must hold a transaction and stop source writes for retirement.
SET LOCAL timezone = 'UTC';
SET LOCAL extra_float_digits = 3;
DO $audit$
BEGIN
    IF EXISTS (
        SELECT 1 FROM "CommonStock" source
        WHERE NOT EXISTS (
            SELECT 1 FROM "EquityDirectorySourceRecord" evidence
            WHERE evidence."Source" = 'common-stock-v1'
              AND evidence."SourceRecordKey" = source."Id"::text
              AND evidence."PayloadJson" = to_jsonb(source)
              AND evidence."PayloadHash" = encode(sha256(convert_to(to_jsonb(source)::text, 'UTF8')), 'hex')
        )
    ) THEN
        RAISE EXCEPTION 'Original directory evidence is incomplete; source rows cannot be retired';
    END IF;
    IF EXISTS (
        SELECT 1 FROM "EquityDirectorySourceRecord"
        WHERE "Source" = 'common-stock-v1'
          AND ("PayloadJson"->>'Id' IS DISTINCT FROM "SourceRecordKey"
            OR "PayloadHash" <> encode(sha256(convert_to("PayloadJson"::text, 'UTF8')), 'hex'))
    ) THEN
        RAISE EXCEPTION 'Original directory evidence has invalid identity or content hashes';
    END IF;
END;
$audit$;
SELECT count(*) AS source_rows FROM "CommonStock";
SELECT count(*) AS preserved_versions, count(DISTINCT "SourceRecordKey") AS preserved_source_ids
FROM "EquityDirectorySourceRecord" WHERE "Source" = 'common-stock-v1';
