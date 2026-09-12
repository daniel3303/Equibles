# Equity identity migration

- `EquityIssuer` represents issuer identity independently of SEC registration; `CommonStockId` bridges all existing issuer facts without moving or rekeying them.
- `EquitySecurity` represents one share class or receipt; `EquityListing` represents one venue listing.
- `LegacyEquityListing` preserves the exact original `(CommonStockId, ListedTicker)` key and maps it to a stable listing ID.
- The backfill covers every legacy issuer, current/reference/secondary symbol, retained ticker alias, and attributable exact series in price, ownership, corporate-action and short-data tables.
- Existing row IDs, financial values, source documents, historical tables, foreign keys and public routes remain unchanged.
- A legacy source key proves only its existing identity; migrated securities remain `Unknown` and listings remain `Legacy` with unknown MIC, currency and quote scale.
- Null and empty-string ticker sentinels remain unattributed and are excluded from listing registration and audit expectations; their original rows and values are retained.
- Unattributed issuer-level rows stay issuer-level; never assign an old ambiguous price, split or holding to today's primary ticker.
- A verified listing requires source evidence, MIC, currency and positive quote scale; legacy migration does not grant verification or international publication.
- Historical symbols and reused tickers keep separate source keys and listing IDs; one symbol never merges different issuers.
- Database writer guards synchronize issuer changes and register newly observed exact historical series from EF, bulk imports and retiring binaries.
- Writer guards are recurring reconciliation and stay installed after the finite backfill completes.
- `EquityIssuerRepository.GetByCik` reads native issuer facts; `EquityDailyStockPriceRepository.GetByListing` reads native `EquityDailyStockPrice` rows by stable listing ID.
- `CommonStockRepository.GetByTicker` reads the registry first and preserves the legacy lookup as a compatibility fallback.
- Legacy customer references remain valid source keys; no customer rows or portfolio economics are rewritten.
- Existing URL aliases and canonicals retain their current MVC behavior; international routes remain a separate exchange-qualified MVC surface.
- Deleting a legacy stock clears only the issuer bridge; registry lineage remains and new identity deletion cannot cascade through retained mappings.
- Both migrations refuse destructive rollback; roll application binaries back with tables and writer guards intact.

## Verification and rollout

- Run `scripts/verify-equity-identity.sql` with `psql -v ON_ERROR_STOP=1` in the financial database; zero missing issuers/attributable series and enabled guards are required.
- This is a finite backfill of existing identities plus recurring write-time synchronization; retire only the backfill procedure after the completion audit passes and old writers are retired, never the guards needed by continuing legacy writers.
- Apply against a restored production backup first and compare every legacy table's row count and content before authorizing production execution.
- Retain a tested database backup and point-in-time recovery through rollout; schema migration creates triggers and can wait for concurrent writers, so schedule a maintenance window sized from the restored-copy rehearsal.
- Deploy the schema and backfill before registry readers; verify completion and existing URL behavior before enabling the new binaries.
- The old storage remains the compatibility representation of the same data; removing it requires a separate, proven migration of every dependent reader and writer.

## Native storage and retirement

- Company facts live on `EquityIssuer`; registration facts and share quantities live on `EquitySecurity`; import checkpoints live on `EquityListing`.
- `EquityIssuerPresentation` selects a default listing for issuer-only requests and refuses listings belonging to another issuer.
- `EquityDailyStockPrice` stores exact listed bars; `UnattributedDailyStockPrice` preserves observations whose original listing is unknown, including IDs shared with a later exact-listed observation.
- Transitional price triggers preserve inserts, resettlements, and deletes atomically while older writers remain deployed.
- `scripts/verify-native-equity-prices.sql` compares every original price field in both directions before enabling native-only writers.
- This is an intermediate implementation: complete financial/customer consumer migration and verified retirement of old tables are required before the task is finished.

## Euronext product identity

- Follow only the supplied official product URL whose ISIN and MIC match the directory record.
- Read issuer code from the product's `custom.instrument` record after matching ISIN, MIC, ticker, product key, and source type; unrelated global settings are not identity evidence.
- Retain the exact instrument JSON and source URL; Euronext's broad `STOCK` type does not establish ordinary-share or receipt form.

## Native quotation basis

- Yahoo chart capture can fill an unknown USD major-unit basis only after exact returned-symbol and unique current U.S. listing ownership checks.
- Preserve source metadata as immutable directory evidence; serialize capture with directory writes and refuse conflicting currency or scale.
- Currency evidence does not verify a MIC or classify the security; current metadata never establishes retired-symbol denomination.
- Capture rides existing chart requests; a finite source-metadata backfill remains a rollout prerequisite for consumers requiring explicit units.

## Source issuer identifiers and Lisbon import

- `EquityIssuerSourceIdentifier` binds an exact provider issuer code to a native issuer and immutable capture evidence; it contains no route records.
- Match existing issuers by exact source identifier, LEI, ISIN, or a current U.S. security CUSIP explicitly connected through GLEIF's complete ISIN-to-LEI relationship set; never match names or old CUSIP aliases.
- Conflicting owners, legal identifiers, venue symbols, or quotation units reject the identity write atomically; a failed import discards its tracked graph.
- Securities remain distinct by ISIN; a shared issuer never merges an ADR with its underlying share, and the source's broad `STOCK` type remains unclassified.
- Preserve existing issuer profiles and presentation listings; new venues retain their own prices and symbols, and native rename history remains intact.
- GLEIF capture requires exact issuer identity, complete unique related ISINs, stable publication/total metadata, and source-provided same-origin pagination without added filters.
- Validate ISIN check digits and LEI MOD 97-10 at both source and native-import boundaries; malformed related identifiers cannot establish ownership through an embedded CUSIP.
- `EquityMarkets:LisbonEnabled` (`EQUITY_MARKETS_LISBON_ENABLED` in Compose) enables daily source reconciliation; unresolved records retry after fifteen minutes without deleting retained identities.
- Enable the worker only after native migrations and exchange-qualified MVC surfaces have passed verification; source acquisition and import alone do not complete the whole-database cutover.
