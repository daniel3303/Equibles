# GLEIF identity captures

- `aspocomp-lei.json` was captured unchanged on 2026-09-22 from `https://api.gleif.org/api/v1/lei-records/743700W8ZIJAMXWWWD26`; it confirms the legal entity, not an ISIN relationship.

- Captured unchanged on 2026-09-12 from `https://api.gleif.org/api/v1/lei-records?filter[isin]=PTALT0AE0002` and the response's related ISIN URL.
- The issuer response identifies LEI `213800AKSTYRLHY3X497`; all six related ISINs state that exact LEI.
- `US02209Y1001` is the Altri ASGSY depositary receipt, with underlying ISIN `PTALT0AE0002`; the identifier relationship alone does not establish a receipt ratio or security classification.
- GLEIF's official API documentation: https://www.gleif.org/en/lei-data/gleif-api.
- The client requests related ISINs with `page[size]=200`, which the API honoured live on 2026-09-15; BNP Paribas (`R0MUWSFPU8MPRO8K5P83`) reported 42,020 ISINs that day, the shape behind the 2,000 enumeration bound.
