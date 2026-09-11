# Equity identity foundation

- `EquityIssuer` represents a company independently of SEC registration; its optional unique `CommonStockId` bridges an existing record without changing legacy reads.
- `EquitySecurity` represents one ordinary share class, preferred share or receipt; its optional ISIN identifies the security rather than the venue.
- `EquityListing` represents one security on a MIC, with an exact ticker, currency, explicit quote multiplier and lifecycle dates.
- Active ticker uniqueness is per MIC; retained inactive listings keep their own IDs when a symbol is reused.
- An identity-source URL is provenance, not a verification verdict; future writers must verify issuer, security and listing relationships from authoritative data.
- Do not infer share classes, receipt ratios, exchanges or currencies from legacy ticker arrays or names.
- The migration creates three empty tables; it does not backfill, rename, copy, delete or update existing stock/price data.
- Legacy stock deletion clears only the optional issuer bridge; new identity survives and issuer/security deletion cannot cascade through retained listings.
- Application rollback retains these additive tables; the migration refuses destructive rollback.
- Existing U.S. controllers, URLs, ticker resolution and price histories remain unchanged.
- No import worker or public reader uses the new tables yet; provider mappings, symbol history, ratio evidence and listing-aware ingestion are subsequent changes.
