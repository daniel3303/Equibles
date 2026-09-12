# Complete equity identity cutover

## Completion contract

- Finish the operational migration across the financial and customer databases in this task; an additive registry alone is not completion.
- Store company facts on EquityIssuer, instrument identity and share counts on EquitySecurity, and venue-specific identity and import state on EquityListing.
- Retain every original record identifier, economic value, source fact, user-authored position, and public URL.
- Give listing-scoped history a stable EquityListingId and issuer-scoped facts an EquityIssuerId; keep source-stated tickers as evidence only where they carry independent meaning.
- Preserve genuinely unattributed observations in a native observation model with explicit unresolved attribution; never manufacture a listing association.
- Move all readers, writers, bulk SQL, uniqueness keys, ingestion checkpoints, and customer references before retiring old storage.
- Remove CommonStock storage, the LegacyEquityListing bridge, the old price representation, and temporary synchronization after the final reconciliation passes and old binaries are gone.
- Retain applied migration history and immutable audit evidence; neither is obsolete application storage.
- Preserve current MVC route templates and canonical U.S. URLs throughout the cutover; exchange-qualified international routes use controllers rather than database route records.

## Data ownership

| Owner | Facts |
|---|---|
| Issuer | Name, description, SEC identifiers, website, fiscal calendar, industry, filings, statements, verified company disclosures |
| Security | Share class or receipt identity, identifier claims, filing registration category/title, shares outstanding and instrument valuation basis |
| Listing | Exchange, ticker, quote currency and scale, listing lifecycle, provider mappings, listing-specific import checkpoints |
| Issuer presentation | Explicit default listing for issuer-only requests; never overrides an explicitly selected listing |
| Listing history | Exact prices, attributed positions, attributed short data, and listing-specific corporate actions |
| Unattributed observations | Original issuer-scoped historical prices/actions/positions whose exact instrument cannot be established from stored evidence |
| Customer records | Stable instrument references with all original quantities, costs, dates, ownership, and subscription settings retained |

## Verification gates

- Back up and decode complete financial and customer archives before rollout; TOC inspection alone does not validate the data blocks.
- Rehearse every schema/data transition on restored production data, including inactive issuers, historical-only symbols, secondary classes, and unresolved observations.
- Compare original rows through explicit field mappings after normalization; row counts alone cannot establish preservation.
- Reconcile competing import checkpoints into one current state while retaining the original checkpoint observations as audit evidence.
- Do not collapse differing security-identifier claims into one value; retain their source, record ID, and current/former/observed semantics.
- Run native-reader and current-URL regression tests, full required CI, and a fresh independent review after the complete implementation.
- Deploy additive storage first, switch all consumers, reconcile again, and perform the contract migration only after old consumers are retired.
- Verify production mappings, preserved histories, customer records, URLs, and new writer behavior before declaring completion.

## Confirmed production cohorts

- All 12,347 issuer rows have an explicit primary ticker; no primary security profile currently lacks its source ticker.
- The pre-listing DailyStockPrice store still contains approximately 10.7 million observations and must be migrated explicitly.
- The financial database has issuer/listing references across filings, facts, ownership, prices, short data, index snapshots, fund disclosures, and ingestion state.
- Customer references include portfolios, subscriptions, recent views, disclosures, calls, extraction state, and source discovery state.
- Retained inactive-directory rows overlap 3,807 current primary records; 2,056 price checkpoints and 22 CUSIP claims differ and cannot be overwritten during consolidation.
- The initial 13-source-table rehearsal caught advisory-lock exhaustion during bulk registration; bulk backfill bypasses per-series advisory locks inside its private migration transaction.

## Implementation evidence

- The initial 13-source-table production restore passed exact source-row hashes and identity reconciliation; it is not a whole-database completion result.
- Native issuer/profile and price integration tests cover an existing populated schema, a 12,000-company backfill, independent venue prices, issuer ownership constraints, and transactional write mirroring.
- Native daily prices preserve exact listing attribution; original issuer-only prices migrate separately and cannot enter an exact price query.
- Full financial and customer backup/restore verification is separate from application migration and must finish before rollout.

## Issuer checkpoint cutover

- Filing-enumeration, financial-facts, and transcript checkpoints reference `EquityIssuer` directly and retain every existing ID and watermark.
- The current physical `CommonStockId` column names remain during mixed-version rollout; C# uses `EquityIssuerId`, and the final contract migration must rename those columns after older binaries retire.
- Native issuer deletion is restricted while checkpoints reference it; retiring a listing or legacy stock cannot erase these watermarks.
- `scripts/verify-native-issuer-checkpoints.sql` checks complete issuer ownership and validated restrictive foreign keys.

## Financial facts and reported statements

- Financial facts and reconstructed statements belong to `EquityIssuer`; repository reads accept issuer IDs without requiring a legacy stock or a listing.
- Retargeting keeps every row in place, including source filing links, dimensional keys, restatement accessions, original numeric values, periods, currencies, scales, JSON payloads, and timestamps.
- Physical issuer columns retain their deployed names until the final contract migration; native issuer foreign keys restrict deletion.
- `scripts/verify-native-issuer-financials.sql` checks complete issuer ownership and validated restrictive foreign keys.

## Remaining route integration

- The native buyback ranking currently carries only a ticker into rendering; native-only issuers and cross-exchange ticker collisions require listing-aware route resolution before rollout.
- Preserve native listing identity through the ranking view model and MVC links, and verify native-only and colliding-ticker rendering during the route cutover.
- A passing storage migration does not make a new listing publicly routable; do not deploy the intermediate consumer cutover before that dependency is complete.

## Filing ownership

- Documents, Form D, N-CEN and attributed N-PORT filings reference native issuers; unlinked trust reports retain their original null owner and registrant CIK.
- Retargeting changes four foreign keys without rewriting source rows or their child relationships.
- The real-schema graph test compares every stored column across documents, binary files, images, artifacts, chunks, embeddings, facts, statements, fund filings and their child rows before and after legacy owner removal.
- That graph test does not authorize deleting legacy owners in production: other unmigrated relationships and price mirroring still require the final contract migration.
- `scripts/verify-native-issuer-filings.sql` checks orphan attribution and validated restrictive native foreign keys after migration.

## Fund-series ownership

- FundSeries now references EquityIssuer through a restrictive foreign key; its previous owner identifier was an unconstrained scalar.
- The migration adds only that foreign key; existing IDs, every stored field, identity-key bytes and public slugs remain unchanged.
- Preserve the historical `cs:` identity-key prefix with the same issuer GUID; renaming an internal model must not create a second fund or replace a URL.
- Native-only issuer materialization is covered through PostgreSQL upsert and two rebuilds; migration coverage compares every stored field and rejects owner deletion.
- `scripts/verify-native-issuer-fund-series.sql` is the read-only completion query; unresolved owners must be zero and the restrictive FK validated before legacy storage retirement.

## Issuer disclosures

- Insider transactions, Form 144 notices, government awards and attributed FDA events reference native issuers with restrictive foreign keys.
- Source-stated security titles, transaction economics, amendment identity, source notes, prior sales, provider keys and retry state remain unchanged; an issuer association does not assert a security or venue.
- Unresolved FDA events retain null issuer attribution; the migration adds no inferred identity.
- The migration changes ownership constraints only; `scripts/verify-native-issuer-disclosures.sql` requires zero missing owners and four validated restrictive constraints before legacy storage retirement.

### Fails-to-deliver observations

- `FailToDeliver` now owns a stable `EquityListingId` and retains `ListedTicker` as source evidence; the native unique key is listing/settlement date.
- The paired migration uses exact legacy mappings and refuses unresolved rows before committing. Observation GUIDs, source tickers, quantities, prices, settlement dates and creation timestamps stay unchanged.
- The old physical owner column and unique index remain only for retiring binaries. Native-only listings have no old owner value, which prevents same-symbol venues from colliding in that temporary index.
- Final cutover removes `equity_ftd_listing_bridge`, `eq_bridge_ftd_listing`, the unmapped `CommonStockId` column and its old unique index together, after all stock-facing readers and writers move to native identities.
- The importer resolves native listing IDs before upsert; native rows survive removal of a legacy stock. Never delete a native listing that retains observations.
- `NativeListingFailsToDeliverTests` verifies full-row preservation, refusal without mutation for unresolved history, same-symbol venue isolation, restrictive deletion, and old/new writer coexistence against PostgreSQL.


## Native FINRA observations

- `DailyShortVolume`, `ShortInterest`, and `OffExchangeVolume` now reference exact native listings with restrictive deletion and listing/date uniqueness.
- Backfill resolves each original issuer/ticker pair; unresolved history aborts the transaction without changing observations.
- Preserve original IDs, source ticker spelling, all quantities/precision, market attribution, timestamps, and every `FinraImportPartition` marker.
- Source-universe hashes keep their existing payload; consumers filter the resolved native listing IDs, and issuer-model inputs require the issuer's primary listing to be a scope member.
- Case-fold repair also requires an exact match with the observation's source ticker, protecting history from a former symbol after a rename.
- Run `scripts/verify-native-finra-listings.sql`; missing identities, mismatches, and duplicate native listing/date groups must be zero, with all three native foreign keys validated and restrictive.
- The unmapped `CommonStockId` columns, old unique indexes, `equity_finra_listing_bridge` triggers and `eq_bridge_finra_listing` function exist only through the retiring-binary window; remove them together in the final contract migration after old consumers stop.
- Completion requires full row reconciliation and the retirement of these bridges; this stage does not complete the database cutover.

## Native corporate-action issuer ownership

- Split and dividend issuer references now target `EquityIssuer` with restrictive foreign keys; original GUIDs and every action field remain unchanged.
- This owner migration does not invent security attribution: exact split source tickers stay exact, unknown split source tickers stay null, and old issuer-level dividends are not assigned to a current share class.
- Preserve source precedence, ratio precision, creation times, applied timestamps, and the dividend amount last incorporated into price history.
- Native-issuer preservation tests compare every stored column across the migration and removal of the old owner, including primary, secondary, and unattributed splits on the same date.
- Exact action attribution and native price writers remain required before final legacy table retirement; this intermediate migration does not complete that cutover.
