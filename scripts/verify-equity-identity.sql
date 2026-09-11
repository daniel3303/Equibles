-- psql -v ON_ERROR_STOP=1 -f scripts/verify-equity-identity.sql
-- Run against the financial database after migration, with all relevant modules installed.
BEGIN READ ONLY;
DO $audit$
DECLARE missing bigint; source_table text; symbol_column text;
BEGIN
  SELECT count(*) INTO missing FROM "CommonStock" stock
    LEFT JOIN "EquityIssuer" issuer ON issuer."CommonStockId" = stock."Id"
    WHERE issuer."Id" IS NULL;
  IF missing > 0 THEN RAISE EXCEPTION '% unmapped legacy issuers', missing; END IF;

  SELECT count(*) INTO missing FROM "CommonStock" stock
    CROSS JOIN LATERAL unnest(ARRAY[stock."Ticker"] || stock."SecondaryTickers" || stock."ReferenceTickers") symbol
    LEFT JOIN "LegacyEquityListing" legacy ON legacy."CommonStockId" = stock."Id" AND legacy."ListedTicker" = symbol
    WHERE symbol IS NOT NULL AND symbol <> '' AND legacy."EquityListingId" IS NULL;
  IF missing > 0 THEN RAISE EXCEPTION '% unmapped directory symbols', missing; END IF;

  SELECT count(*) INTO missing FROM "LegacyEquityListing" legacy
    JOIN "CommonStock" stock ON stock."Id" = legacy."CommonStockId"
    JOIN "EquityListing" listing ON listing."Id" = legacy."EquityListingId"
    JOIN "EquitySecurity" security ON security."Id" = listing."EquitySecurityId"
    JOIN "EquityIssuer" issuer ON issuer."Id" = security."EquityIssuerId"
    WHERE issuer."CommonStockId" IS DISTINCT FROM stock."Id";
  IF missing > 0 THEN RAISE EXCEPTION '% mappings point at another issuer', missing; END IF;

  IF NOT EXISTS (SELECT 1 FROM pg_trigger WHERE tgname = 'equity_identity_issuer_write'
      AND tgrelid = '"CommonStock"'::regclass AND tgenabled = 'O') THEN
    RAISE EXCEPTION 'Legacy issuer writer guard is not enabled';
  END IF;

  FOR source_table, symbol_column IN SELECT * FROM (VALUES
    ('ListedDailyStockPrice', 'ListedTicker'),
    ('CommonStockDelistedListing', 'ListedTicker'),
    ('CommonStockTickerAlias', 'Ticker'),
    ('CommonStockTickerEvidence', 'Ticker'),
    ('CommonStockListedCusip', 'ListedTicker'),
    ('StockSplit', 'PriceSeriesTicker'),
    ('InstitutionalHolding', 'ListedTicker'),
    ('StockQuarterlyListingActivity', 'PriceSeriesTicker'),
    ('DailyShortVolume', 'ListedTicker'),
    ('ShortInterest', 'ListedTicker'),
    ('OffExchangeVolume', 'ListedTicker'),
    ('FailToDeliver', 'ListedTicker')
  ) sources(table_name, column_name)
  LOOP
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public'
        AND table_name = source_table AND column_name = symbol_column) THEN
      EXECUTE format('SELECT count(*) FROM (SELECT DISTINCT "CommonStockId", %I AS symbol FROM %I WHERE nullif(%I, '''') IS NOT NULL) source
        JOIN "CommonStock" stock ON stock."Id" = source."CommonStockId"
        LEFT JOIN "LegacyEquityListing" legacy ON legacy."CommonStockId" = source."CommonStockId" AND legacy."ListedTicker" = source.symbol
        WHERE legacy."EquityListingId" IS NULL', symbol_column, source_table, symbol_column) INTO missing;
      IF missing > 0 THEN RAISE EXCEPTION '% has % unmapped series', source_table, missing; END IF;
      IF NOT EXISTS (SELECT 1 FROM pg_trigger WHERE tgname = 'equity_identity_series_write'
          AND tgrelid = format('%I', source_table)::regclass AND tgenabled = 'O') THEN
        RAISE EXCEPTION '% writer guard is not enabled', source_table;
      END IF;
    END IF;
  END LOOP;
  RAISE NOTICE 'PASS: all legacy issuers and attributable exact series are mapped; writer guards are enabled.';
END $audit$;
COMMIT;
