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

## Native congressional-trade issuers

- Congressional trades retain nullable issuer associations under `EquityIssuer`; removing a legacy company row no longer clears a resolved association.
- The restrictive native foreign key preserves historical ownership; unresolved issuer GUIDs remain null.
- Preserve the filed ticker, complete or partial source-row identity, original trade GUID, all filed amounts and metadata, timestamps, and import/filing ledgers without replay or reclassification.
- Dated SEC evidence continues to decide issuer resolution; present-day ticker spelling never fills an unresolved association.
- The physical `CommonStockId` column remains only through the retiring-binary window and is renamed, without changing values, in the final contract migration.

## Native issuer identity evidence

- `EquityIssuerTickerEvidence` preserves immutable dated SEC symbol observations; `IssuerSecurityRegistration` preserves the latest filed title/symbol/exchange statement for each issuer and normalized symbol.
- Both reference native issuers restrictively and retain original IDs, source document IDs, accession numbers, dates, text, and existing unique keys.
- A filing can capture evidence and registrations after its legacy company row is retired; classification updates only the native issuer's explicitly selected security.
- Dated ticker resolution, normalization, extraction versions, registration freshness, and no-update evidence upserts remain unchanged.
- Physical table names `CommonStockTickerEvidence` and `ListedSecurity`, and their `CommonStockId` columns, remain only for retiring binaries; rename them without copying or deleting source rows during the final contract migration.

## Native holdings issuers

- Institutional holdings and all three quarterly activity stores reference native issuers restrictively, preserving original owner GUIDs.
- Every holding field, manager allocation, original CUSIP/listed ticker, valuation and retry marker remains unchanged; no parser replay is used for migration.
- Previously unconstrained summary rows retain an absent owner's original GUID in an issuer record with unknown metadata.
- Batch writes validate native issuer existence; retiring a legacy directory entry after source resolution cannot silently drop its position.
- PostgreSQL preservation tests compare every column in holdings, manager legs, and all summary stores across migration and legacy-owner retirement.
- `scripts/verify-native-issuer-holdings.sql` requires zero missing issuers and four validated restrictive ownership constraints; exact attribution and final directory retirement remain separate completion requirements.
- Exact security/listing attribution and remaining legacy directory consumers still require migration before final storage retirement.

## Native daily-price readers and writers

- Current price consumers read `EquityDailyStockPrice` by stable listing ID; issuer-level consumers explicitly select the presentation listing.
- Yahoo resolves each registered source listing under the ownership lock, preserving resettlement, split-basis, complete-history, and concurrent-writer checks.
- `EnableNativePriceWriters` mirrors registered legacy series into `ListedDailyStockPrice` for retiring readers; every field, ID, update, deletion, and rollback shares the original transaction.
- Native-only foreign series remain isolated even when they share an issuer and ticker with a U.S. series.
- The finite retirement cohort is every exact legacy price and every unattributed legacy price; `scripts/verify-native-equity-prices.sql` compares complete rows in both directions before retirement.
- Remove both old price tables, old model mappings, translation methods, and temporary synchronization functions after all old binaries are gone and reconciliation passes; recurring provider repair and reconciliation remain active.

## Native directory identity evidence

- Retired issuer CUSIPs, source-stated secondary CUSIPs, retired ticker aliases, and delisting observations reference native issuers through restrictive foreign keys.
- The migration changes only ownership constraints; all source row IDs, symbols, CUSIPs, dates, ambiguous candidate arrays, and importer checkpoints remain byte-for-byte equivalent.
- Identifier evidence remains distinct from an asserted security identity; an issuer-level historical claim cannot establish an otherwise unknown share class.
- `scripts/verify-native-directory-evidence.sql` requires zero missing issuers and four validated restrictive constraints.
- Historical price completion reads the exact native listing checkpoint; another venue's matching symbol cannot complete the source series.
- Physical legacy table/column names and compatibility triggers retire with the final directory cutover; source evidence and URL aliases remain preserved in native storage.

## Native market and issuer directory keys

- `EquityListing.MarketCountryCode` identifies the trading venue's country independently of issuer domicile and quotation currency.
- `AddNativeDirectoryIdentity` backfills only exact pre-international U.S. mappings; native-only foreign listings remain untouched.
- Unqualified ticker queries use explicit U.S. listings; multiple matches remain visible for the resolver to reject ambiguity.
- The temporary mapping trigger maintains country for retiring U.S. writers and rejects adoption of a foreign listing under their identity.
- `EquityIssuer.LegalEntityIdentifier` stores optional source-backed issuer identity; a ticker, name, or currency cannot establish that identity.
- Finite completion query: `scripts/verify-native-directory-identity.sql` requires `missing_listings = 0` and `wrong_market = 0`.
- Retire the country backfill trigger and legacy mapping after native directory writers replace every old writer; preserve the native keys and source evidence.


## Native issuer and directory consumers

- Runtime issuer readers and source writers use `EquityIssuerRepository` and `EquityIdentityManager`; `CommonStock` remains a historical schema mapping only until the final contract migration.
- Issuer profile data lives on `EquityIssuer`, instrument characteristics on `EquitySecurity`, and trading identity/status/checkpoints on `EquityListing`.
- The U.S. directory query excludes issuer-only records; SEC ingestion uses CIK identity independently of market listing eligibility.
- Directory updates serialize under a database advisory lock and issuer row lock. Refresh refuses pending issuer, security, listing, or presentation changes before reloading any tracked values.
- A source primary-symbol change selects a new listing while retaining the old listing, security, and all attached observations. Foreign listing rows remain unchanged.
- Failed directory saves restore both database state and the tracked identity graph before another save can occur.
- Explicit GUID generation is application-owned for issuer, security, and listing entities; `PreserveAssignedEquityIdentityKeys` changes EF metadata without changing stored rows.
- U.S. source-symbol readers use native listings directly; ambiguous same-owner/same-symbol U.S. matches cannot merge histories.
- Retained historical migration fixtures continue to seed the historical schema. An unattributed FINRA row still triggers an atomic migration refusal; its unknown identity must be resolved or preserved explicitly before final cutover.

## Split capture and historical basis

- New split observations reference the locked exact listing; source symbols remain unchanged after a listing rename.
- Only one exact recorded U.S. listing may receive previously missing source attribution; unresolved original events remain separate and unchanged.
- An unknown historical split prevents price comparisons or history replacement across its date; it never supplies a restatement ratio.
- Full native reconciliation and dividend capture must precede foreign price writers.

## Native dividend capture and action reconciliation

- New cash dividends carry the exact native listing and an explicit major-unit currency; capture locks directory ownership and validates the current symbol, denomination, and lifecycle.
- Historical issuer-only dividends retain every original field and marker. A source recapture creates a separate native observation; no issuer/ex-date match can claim the original row.
- Same-date components require one currency and positive amounts throughout; a partial or conflicting group is refused as a whole.
- The U.S. Yahoo boundary requires the returned exact symbol plus USD/exchange/timezone metadata. The U.S. reference adapter supplies explicitly USD amounts. Neither boundary guesses foreign dividend units.
- Native reconciliation selects both action types by stable listing ID and advances a durable listing-ID cursor. Currency and identity are part of the selected dividend snapshot; primary designation never transfers the payment or its marker.
- A renamed symbol invalidates an in-flight response; a later request follows the same listing ID under its current symbol. Historical capture and stamping revalidate the exact retirement cutoff.
- Current dividend history and derived dividend inputs read only the requested/presentation listing's attributed USD payments. Preserve the original issuer-only rows as evidence; complete source replay and coverage/value comparison before deploying these readers.
- `KeyCorporateActionCursorByListing` adds the native cursor without removing old fields; retire their physical columns with the final contract only after the retiring worker is gone.
- Historical reconciliation targets carry native listing IDs and retain the retirement-evidence ID separately; exact lifecycle and cutoff checks guard the selected series before fetching and writing.

- Yahoo price targets retain the pre-fetch native listing ID through quotation evidence, action capture, and price-write revalidation; a ticker reassignment during the request cannot attach the old response to its new owner.
- Existing-payment currency conflicts fail without changing the original amount, source, or reconciliation markers.

## Verified Lisbon daily prices

- Queue active verified PT listings on XLIS, ENXL or ALXL with explicit EUR major units and an ISIN.
- Yahoo's published `.LS` suffix supplies only a candidate; exact returned symbol, EUR, LIS, EQUITY and Europe/Lisbon metadata gate prices and actions.
- A competing active ticker claim in any of the three source MICs blocks capture, including unverified claims.
- Retain immutable `yahoo-lisbon-chart-v1` metadata evidence and revalidate native identity, ISIN, MIC and units under the directory lock.
- Incremental writes and full-history corporate-action reconciliation retain native listing IDs without requiring issuer presentation or legacy stock rows.
- Returned settled dates establish observations; no U.S. holidays, inferred Lisbon sessions or synthetic gap bars apply.
- Explicit worker filters use `MIC:TICKER` for Lisbon; unqualified filters remain U.S.-only.
- Captured-source replay queued 44 verified listings, retained 165 bars across 41 listings with exact parsed OHLCV values, and retained 44 source captures. A second import preserved all IDs and values, and the existing U.S. ADR stayed unchanged.
- The source replay used an isolated PostgreSQL database; production rollout and final legacy-table retirement remain pending.
- Each split/dividend transaction revalidates the complete pre-fetch source binding under its own directory lock; an earlier quotation check cannot authorize a later action write.

## Explicit split attribution in regression fixtures

- Restatement fixtures bind each known split to its exact native listing; issuer-only observations cannot provide a primary-listing ratio.
- Unattributed splits keep both primary and secondary holding values pending while retaining filed quantities.
- Form 144 current-basis percentages remain absent across unresolved split attribution; filed shares and market values remain unchanged.
- Off-exchange volume output accepts absent explanatory notes without failing valid history requests.

## Unqualified filing search

- Unqualified filing-list, BM25, vector and PostgreSQL fallback searches require matching U.S. issuer claims; equal foreign tickers cannot enter their result sets.
- Recorded U.S. co-registrants still contribute their filings, and direct issuer/document-ID reads retain foreign records.
- Existing ticker index predicates remain in each search arm; market ownership is an additional native identity check.

## Per-share financial facts

- History, statements and peer comparisons select split ratios by the presentation listing ID; a foreign listing with the same ticker cannot restate its values.
- Source symbols remain evidence after a rename; stable listing identity decides attribution.
- Unattributed, conflicting or invalid post-filing splits preserve the filed value with an explicit unresolved-basis label; no original financial fact is rewritten.

## Current directory snapshots

- A complete Lisbon directory snapshot atomically withdraws capture eligibility for absent ISIN/MIC/ticker identities before product imports begin; it does not infer an effective delisting date.
- The current snapshot record fences every subsequent product import, so superseded work cannot reactivate an old symbol.
- A returning exact security/venue reuses its sole undated inactive listing; explicit retirement dates and ambiguous episodes never collapse.
- Source snapshots, original listing IDs and attached prices survive disappearance and symbol reuse.

## Price history during directory retirement

- Native bars accept the current exact ticker or a retained alias while the temporary mirror preserves the original legacy series key.
- A legacy directory deletion cannot cascade into either native price archive; direct price replacements still synchronize while the owner exists.
- Full-row reconciliation reports retained histories without a legacy owner separately; compare that cohort against the immutable pre-cutover export rather than treating its absent counterpart as proof.

## Canonical physical owner columns

- Issuer owners now map to physical `EquityIssuerId` columns: 27 OSS financial tables, 53 commercial financial tables including those OSS tables, and 34 customer tables.
- This is an additive transition; the older ownership columns, indexes and foreign keys remain synchronized until all binaries use canonical storage. A direct rename before rollout would break running readers.
- A transaction-local mirror rejects conflicting dual-owner writes and updates both column generations before existing write-time guards run. Existing column-specific update triggers watch both owner columns during the transition.
- The finite backfill commits 10,000-key batches with its `EquityOwnerMigrationProgress` cursor; interrupted batches roll back together and committed batches are not repeated.
- New indexes build concurrently under distinct names; retries retain valid builds and repair only interrupted invalid builds. Old indexes remain usable until final retirement. Canonical constraints are added and committed before separate validation scans, releasing the addition locks before scanning. Table expansion commits separately with a five-second lock timeout.
- Run the matching `verify-canonical-equity-owners*.sql` script for exact owner equivalence, valid indexes, validated constraints and every completion checkpoint. Full original-row exports remain a separate conservation gate.
- Apply through the migration runner or an exact non-idempotent migration range; a generic idempotent SQL wrapper cannot enclose the backfill's batch commits. The operations themselves resume an interrupted run.
- Retire both owner generations' mirrors and checkpoints only after those queries pass, all consumers use canonical names, and full restored-data plus production verification passes.

## Native evidence table storage

- The six issuer alias, ticker evidence, listing CUSIP, retirement evidence and security registration models use their native physical table names.
- Empty native tables, foreign keys and indexes are created and committed before copying, releasing the issuer-table schema lock.
- Each source table is copied in its own transaction with exact column-shape, redundant-owner and bidirectional full-row comparisons; an unexpected source column or conflicting target row stops the migration.
- The transaction briefly blocks writes to that evidence table while copying and installing two-way row mirrors; reads remain available. The largest current cohort is approximately 603,000 ticker-evidence rows, 155 MB including indexes; measure this copy on the restored production database before rollout.
- Existing and native writers retain real tables and unique indexes for their upserts. Unchanged mirrored rows do not recurse; IDs, source payloads, nulls, arrays and repair state remain intact.
- Run `scripts/verify-native-equity-evidence.sql` during the compatibility window. Retire the old six tables and their temporary mirror functions after all writers use native storage and final reconciliation passes.

## Issuer foreign-key rollout

- The unshipped issuer-retarget migrations atomically replace each old owner constraint with an enforced `NOT VALID` native constraint, then commit metadata locks before validating existing rows.
- Validation allows ordinary issuer and observation reads/writes; an interrupted validation reuses the same checked constraint definition and preserves every original row.
- The retiring directory can no longer cascade-delete observations after metadata commit; native issuer ownership already protects those rows before the validation scan finishes.
- Applied production migration history is unchanged; this transaction-boundary correction belongs only to the new international-identity migration sequence.
