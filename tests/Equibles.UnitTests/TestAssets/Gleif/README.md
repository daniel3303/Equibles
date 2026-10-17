# GLEIF identity captures

- `aspocomp-lei.json` was captured unchanged on 2026-09-22 from `https://api.gleif.org/api/v1/lei-records/743700W8ZIJAMXWWWD26`; it confirms the legal entity, not an ISIN relationship.

- Captured unchanged on 2026-09-12 from `https://api.gleif.org/api/v1/lei-records?filter[isin]=PTALT0AE0002` and the response's related ISIN URL.
- The issuer response identifies LEI `213800AKSTYRLHY3X497`; all six related ISINs state that exact LEI.
- `US02209Y1001` is the Altri ASGSY depositary receipt, with underlying ISIN `PTALT0AE0002`; the identifier relationship alone does not establish a receipt ratio or security classification.
- GLEIF's official API documentation: https://www.gleif.org/en/lei-data/gleif-api.
- The client requests related ISINs with `page[size]=200`, which the API honoured live on 2026-09-15; BNP Paribas (`R0MUWSFPU8MPRO8K5P83`) reported 42,020 ISINs that day, the shape behind the 2,000 enumeration bound.

- Captured on 2026-10-17 for `GleifEntityClient`:
  - `samsung-electronics-fulltext.json` comes from `https://api.gleif.org/api/v1/lei-records?filter[fulltext]=Samsung Electronics Co. Ltd.&page[size]=10`. Three of the nine records are kept unchanged (the Korean parent `9884007ER46L6N7EI764`, an asset manager, and a Taiwan subsidiary with a transliterated name), and the pagination is set to match.
  - `aws-ultimate-parent.json` (unchanged) comes from `/api/v1/lei-records/2549000I2PRQGGIGCA75/ultimate-parent`. It names Amazon.com, Inc.
  - `google-direct-parent.json` (unchanged) comes from `/api/v1/lei-records/7ZW8QJWVPR4P1J1KQY45/direct-parent`. It names Alphabet Inc.
  - `alphabet-ultimate-parent-404.json` (unchanged) is the 404 body from `/api/v1/lei-records/5493006MHB84DD0ZWV18/ultimate-parent`, which GLEIF returns for an entity with no reported parent.
