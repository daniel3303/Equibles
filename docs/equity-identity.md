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

## Source issuer identifiers and market directory import

- `EquityIssuerSourceIdentifier` binds an exact provider issuer code to a native issuer and immutable capture evidence; it contains no route records.
- Match existing issuers by exact source identifier, LEI, ISIN, or a current U.S. security CUSIP explicitly connected through GLEIF's complete ISIN-to-LEI relationship set; never match names or old CUSIP aliases.
- Conflicting owners, legal identifiers, venue symbols, or quotation units reject the identity write atomically; a failed import discards its tracked graph.
- Securities remain distinct by ISIN; a shared issuer never merges an ADR with its underlying share, and the source's broad `STOCK` type remains unclassified.
- Preserve existing issuer profiles and presentation listings; new venues retain their own prices and symbols, and native rename history remains intact.
- GLEIF capture requires exact issuer identity, complete unique related ISINs, stable publication/total metadata, and source-provided same-origin pagination without added filters; related ISINs are read 200 a page at a shared pace of 60 requests a minute, with throttled and failed requests retried after an exponential backoff.
- An issuer whose reported ISIN total exceeds 2,000 is not enumerated: the capture records only the requested ISIN, which the lookup itself confirmed, plus the reported total. Such an issuer still joins an existing row by source identifier, LEI or the exact ISIN, but not through a sibling ISIN or a sibling's embedded CUSIP, so a row known only under a sibling identifier (a depositary receipt captured without an LEI) would be created a second time rather than joined or refused.
- Validate ISIN check digits and LEI MOD 97-10 at both source and native-import boundaries; malformed related identifiers cannot establish ownership through an embedded CUSIP.
- Markets are code-owned in `EquityMarketCatalog` (venue set, country, currency, provider identity, session times); each has one `EquityMarketRegistration` row recording its pass state, and `DirectoryRefreshRequestedAt` forces an early pass. Workers read the table every cycle, never a host setting.
- A directory row becomes a listing only when the FIRDS universe (ESMA plus FCA, refreshed by `FirdsUniverseWorker`) lists its ISIN as a live share (CFI `ES*` or `EP*`) on one of the market's venue codes (`EquityMarket.FirdsVenueCodes`; FIRDS files Xetra by segment, never as `XETR`, a Nasdaq Nordic line under its lit book, Nordic@Mid and Auction on Demand segments or the First North SME growth-market code, and a Madrid line on XMAD or its dark midpoint book) and `EquityMarketDirectoryGate` confirms the market is the share's home: a directory that states a primary market (Xetra's `Primary Market MIC Code`) decides, unless FIRDS places the share's relevant venue outside the market's home venues and its competent authority in another country; Euronext's product link names the venue it homes each line on (a cross-listed line's market cell lists every Euronext venue quoting it, `XBRU, XPAR`, and links the home venue), and the Euronext source states a primary market only when that venue is a sibling market's, so such a row is skipped as that market's, while a line homed on the market defers to FIRDS' relevant trading venue rather than engaging the authority escape, which a Euronext link says nothing about. Every other row is counted as skipped, never failed, and unresolved rows retry on the next pass without deleting retained identities.
- A verified listing whose directory row, product URL and symbol are unchanged is re-verified against the product page and GLEIF every thirty days; any change re-verifies at once.
- Give a catalog market a `DirectorySource` only after native migrations and exchange-qualified MVC surfaces have passed verification, because that is what starts its capture; source acquisition and import alone do not complete the whole-database cutover.

## Corporate action source listings

- Actions retain their original issuer, symbol, amounts, dates, provenance, and applied markers; nullable listing identity preserves unresolvable historical evidence.
- Backfill a split only when its exact recorded U.S. symbol or alias identifies one listing and no second retained event claims that listing/date.
- Historical dividends have no recorded listing or currency; leave both absent until source-backed capture supplies a separate attributed observation.
- Exact native actions are unique per listing/date; unattributed source rows retain their original uniqueness without blocking another venue.
- Recorded listing attribution is immutable, and reverse directory changes cannot invalidate an action's ownership or dividend denomination.
- Action validation locks its listing/security while checking ownership; native reads select an exact listing and never include sibling or unattributed events.
- Capture and reconciliation consumers must complete their native transition before foreign price writes are enabled.

## Mixed-version profile writes

- Legacy updates propagate only changed profile fields and changed per-listing memberships.
- Unrelated writes preserve newer native share counts, capitalization, checkpoints, lifecycle and presentation choices.
- Full profile copying remains limited to initial insertion and the historical backfill.

## Obsolete directory claims

- SEC ticker reassignment withdraws every obsolete U.S. directory claim under the locked issuer.
- Independent reference coverage protects its exact listing; foreign listings and all historical observations remain intact.
- A newly acquired reference claim on the displaced symbol refuses retirement when the locked graph is refreshed.

## Market capture

- Every catalog market with a directory adapter is captured; there is no per-market switch. `EquityMarketDirectoryWorker` runs each such market's directory pass within a minute of start-up, once `FirdsUniverseWorker` has stored a full set for the market's authority, and again once a day.
- Prices, quotation evidence and corporate actions are captured for every verified listing on a catalog market; a verified listing only exists because a directory pass created it.
- `EquityMarketRegistration` holds one row per catalog market recording pass state only: the last refresh, the last directory counts, the last error, and a refresh request. Setting `DirectoryRefreshRequestedAt = now()` on an adapter market's row runs its pass on the next control tick.
- The `Enabled` column is retired and unread; a later migration drops it.

## Current directory across markets

- `EquityIssuerRepository.GetCurrentDirectory()` is the live directory across every market: an issuer whose presentation listing is active and is either a US listing or a venue listing a directory adapter verified (`IdentityState == Verified`). A legacy non-US presentation belongs to neither directory. `GetCurrentUsDirectory()` keeps its US-only universe for the surfaces that are US by contract (screener, indexes, MCP, REST, ALVIS, sitemaps).
- `GetCurrentDirectoryIssuer(id)` and `GetCurrentDirectoryByIds(ids)` are the single-issuer and batch forms; `IsCurrentDirectoryIssuer(issuer)` is the in-memory twin for an already loaded graph.
- `EquityListingSymbol` is the one spelling of a listing for people, logs and file names: the bare ticker for a US listing, `MIC:TICKER` for a venue listing (`MIC-TICKER` where a file name needs it). A directory ticker never contains a colon, so the display form cannot collide with a ticker.
- Website discovery draws its candidates from the current directory and hands each source the issuer's LEI, ISIN, venue code and market country. The SEC filings source skips a CIK-less issuer, Wikidata joins a CIK-less issuer on its LEI (property P1278) and an SEC registrant on its CIK only, and the Yahoo profile source asks by the catalog provider symbol (`AIR.PA`), never the bare ticker. `StockWebsiteDiscovered` carries the venue code of a verified listing and null for a US listing.
- Yahoo enrichment (key statistics and company profile) targets the presentation listing of every verified venue issuer, stamped through the same `YahooEnrichmentAttemptedAt` cadence as US listings, and asks Yahoo by the provider symbol; a listing outside the catalog has no symbol and is skipped. `EquitySecurity.MarketCapitalization` is stored as reported, in major units of the presentation listing's trading currency, so rank-only readers tolerate the mix and sums across markets may not.

## Holdings replay identity

- Native import preflight retains the stored full-grain/CUSIP observation key before assembling both position and manager writes.
- A permanent insert guard refuses incompatible keys from unprepared writers before they can alter either positions or another security’s allocations.
- Both generations serialize on native issuer locks; original position IDs, source facts and attribution IDs remain intact through replay and final storage retirement.
