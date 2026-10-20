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
- Captured unchanged on 2026-10-17 for `GleifEntityClient.Suggest`:
  - `samsung-electronics-autocomplete.json` comes from `https://api.gleif.org/api/v1/autocompletions?field=fulltext&q=Samsung Electronics Co. Ltd.`. Two of its five suggestions ("SAMSUNG ELECTRONICS CO., LTD" and "Thai Samsung Electronics Co., Ltd.") carry no LEI.
  - `samsung-electronics-lei-records.json` comes from `/api/v1/lei-records?filter[lei]=254900SHHC9DE6P8N928,2549000PB5BA9MOMBQ96,254900UDOY5DHMH56386&page[size]=3`. GLEIF returns the three records in the reverse of the requested order.
- On the same day, GLEIF's full-text search for "Google" did not return GOOGLE LLC (`7ZW8QJWVPR4P1J1KQY45`) in its first ten records, while autocomplete suggested it first.
- Captured unchanged on 2026-10-20 for `GleifEntityClient.GetRegistered`:
  - `toyota-registered-as.json` comes from `https://api.gleif.org/api/v1/lei-records?filter[entity.registeredAs]=1803-01-018771&page[size]=10`. Its one record is Toyota Motor Corporation (`5493006W3QUS5LMH6R84`), filed in Japan's commercial register (`RA000412`) under 1803-01-018771, which is Toyota's corporate number `1180301018771` without its leading check digit.
- Captured unchanged on 2026-10-20 for `GleifIdentityClient.GetIsinsForLei`:
  - `toyota-lei-record.json` comes from `https://api.gleif.org/api/v1/lei-records/5493006W3QUS5LMH6R84`.
  - `toyota-isins.json` comes from `/api/v1/lei-records/5493006W3QUS5LMH6R84/isins?page[size]=200`. It relates 22 ISINs to Toyota Motor: its ordinary share `JP3633400001`, eleven other Japanese ISINs and ten US ones.
