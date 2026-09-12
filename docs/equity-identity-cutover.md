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
