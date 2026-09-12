\set ON_ERROR_STOP on
BEGIN READ ONLY;
DO $audit$
DECLARE table_name text; missing bigint; checked integer;
BEGIN
  FOREACH table_name IN ARRAY ARRAY['Document', 'FormDFiling', 'NCenFiling', 'NportFiling'] LOOP
    EXECUTE format('SELECT count(*) FROM %I filing LEFT JOIN "EquityIssuer" issuer ON issuer."Id" = filing."CommonStockId" WHERE filing."CommonStockId" IS NOT NULL AND issuer."Id" IS NULL', table_name) INTO missing;
    IF missing <> 0 THEN RAISE EXCEPTION '% has % orphan issuer filings', table_name, missing; END IF;
    SELECT count(*) INTO checked FROM pg_constraint
      WHERE conrelid = format('public.%I', table_name)::regclass AND contype = 'f'
        AND confrelid = 'public."EquityIssuer"'::regclass AND convalidated AND confdeltype = 'r';
    IF checked <> 1 THEN RAISE EXCEPTION '% lacks its validated restrictive issuer foreign key', table_name; END IF;
  END LOOP;
END $audit$;
COMMIT;
