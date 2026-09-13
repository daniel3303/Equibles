-- Audit body only: the caller owns the transaction and source locks at final retirement.
-- Preserve exact original composite keys and every stored cursor field, including timestamps.
DO $audit$
DECLARE missing bigint; invalid bigint;
BEGIN
    SET LOCAL timezone = 'UTC';
    SET LOCAL extra_float_digits = 3;
    SELECT count(*) INTO missing FROM "LegacyEquityListing" r
    WHERE NOT EXISTS (
        SELECT 1 FROM "EquityDirectorySourceRecord" e
        WHERE e."Source" = 'legacy-equity-listing-v1'
          AND e."SourceRecordKey" = jsonb_build_array(r."CommonStockId", r."ListedTicker")::text
          AND e."PayloadJson" = to_jsonb(r)
          AND e."PayloadHash" = encode(sha256(convert_to(to_jsonb(r)::text, 'UTF8')), 'hex')
    );
    IF missing <> 0 THEN RAISE EXCEPTION '% original listing mappings are not preserved', missing; END IF;

    SELECT count(*) INTO missing FROM "CorporateActionPriceReconciliationCursor" r
    WHERE NOT EXISTS (
        SELECT 1 FROM "EquityDirectorySourceRecord" e
        WHERE e."Source" = 'corporate-action-cursor-v1' AND e."SourceRecordKey" = r."Name"
          AND e."PayloadJson" = to_jsonb(r)
          AND e."PayloadHash" = encode(sha256(convert_to(to_jsonb(r)::text, 'UTF8')), 'hex')
    );
    IF missing <> 0 THEN RAISE EXCEPTION '% original cursor rows are not preserved', missing; END IF;

    SELECT count(*) INTO invalid FROM "EquityDirectorySourceRecord" e
    WHERE e."Source" IN ('legacy-equity-listing-v1', 'corporate-action-cursor-v1')
      AND (e."PayloadHash" IS DISTINCT FROM encode(sha256(convert_to(e."PayloadJson"::text, 'UTF8')), 'hex')
        OR e."SourceRecordKey" IS DISTINCT FROM CASE e."Source"
            WHEN 'legacy-equity-listing-v1' THEN jsonb_build_array(e."PayloadJson"->>'CommonStockId', e."PayloadJson"->>'ListedTicker')::text
            ELSE e."PayloadJson"->>'Name' END);
    IF invalid <> 0 THEN RAISE EXCEPTION '% retained identity source hashes or keys are invalid', invalid; END IF;
END;
$audit$;

SELECT "Source", count(*) AS preserved_versions,
    count(DISTINCT "SourceRecordKey") AS preserved_original_keys
FROM "EquityDirectorySourceRecord"
WHERE "Source" IN ('legacy-equity-listing-v1', 'corporate-action-cursor-v1')
GROUP BY "Source" ORDER BY "Source";
