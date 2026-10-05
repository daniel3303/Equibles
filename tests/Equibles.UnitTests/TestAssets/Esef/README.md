# European filing index captures

- `ctt-2022-json-excerpt.json` retains the source document metadata and unchanged facts `f-3`/`f-4` from `https://filings.xbrl.org/529900G4A1IKOKC22K56/2022-12-31/ESEF/PT/0/002367-2022-12-31.json`, captured on 2026-09-21; all other facts are omitted. The xBRL-JSON midnight instants correspond to the preceding calendar day, and `decimals` is precision, not a scaling factor.
- `arkema-2020-json-excerpt.json` retains the source document metadata and unchanged first fact `fact_16961` from `https://filings.xbrl.org/9695000EHMS84KKP2785/2020-12-31/ESEF/FR/0/arkema-2021-12-31AR.json`, captured on 2026-09-22; all other facts are omitted. The report declares the published 2021-02-03 candidate-recommendation document type.

- Captured on 2026-09-17 with plain GETs (`Accept: application/vnd.api+json`, no cookies, no key) from `https://filings.xbrl.org/api/filings`, the JSON:API index the host publishes over the whole European corpus.
- `filings-fr-page.json` is the unchanged reply of `?include=entity&filter[country]=FR&page[size]=3&page[number]=1`. It states `meta.count` 1179 for France and carries three filings with their `included` entity resources; the filer's LEI is the entity's `identifier`, never a fragment of the composite `fxo_id`.
- `filings-ua-page.json` is the same call for Ukraine (`meta.count` 9782). Those rows are `UAIFRS`, not ESEF, and their entity identifiers are bare EDRPOU registry numbers rather than LEIs, so they are what the regime and identifier gates exist to refuse.
- The index states no regime FIELD. The regime is the third-from-last hyphen-separated part of `fxo_id` (`{identifier}-{period}-{regime}-{country}-{index}`) and of the stored path beside it, which is why it is read from the end of the key: a Ukrainian identifier itself contains a hyphen.
- The whole corpus was 25,912 filings that day. Germany and Ireland return `meta.count` 0 under every spelling, so neither Xetra nor Euronext Dublin is covered by this source.
- `filings-one-issuer.json` is the unchanged reply of `?include=entity&filter[entity.identifier]=529900S21EQ1BO4ESM68&page[size]=20`, TotalEnergies. It states 8 filings: the same annual report filed in BOTH France and Great Britain for each of 2022, 2023, 2024 and 2025, every one with `error_count` 0. For the 2024 period the British filing was added first (11:53) and the French one later (12:06), so a rule that only took the earliest addition would pick the wrong country for a Paris-listed issuer.
- `filings-two-issuers.json` is the one file here that is DERIVED rather than captured. It is TotalEnergies' own two 2025 rows from the capture above, mirrored onto a second real LEI (Izostal's, `259400NFU8A8SBP6VC21`) with the identifier substituted through the addresses, the key and a second `included` entity resource. The corpus is a single index over every issuer, and no single-issuer call can show a pass reaching two of them, which is what the budget, the failure isolation and the paging need.
- `izs-2022-excerpt.xhtml` is cut from a real report, Izostal S.A.'s 2022 consolidated statements at `https://filings.xbrl.org/259400NFU8A8SBP6VC21/2022-12-31/ESEF/PL/0/izs_2022-12-31_pl/reports/izs_2022-12-31_pl.xhtml`, captured the same day. Its `<html>` tag with the report's own namespaces, its `ix:header`, one `@font-face` rule, one `<img>` and the consolidated balance-sheet table are all verbatim; only the two encoded payloads are cut short, to 2,048 and 512 characters, because the whole file is 10.4 MB of which the font alone is 7.5 MB. The quoted `"data: 31.12.2022"` in the sentence before the image is added, and is the prose an encoded-payload rule must not mistake for one.

## Nostrum 2025 empty title

`nostrum-2025-empty-title.xhtml` retains the namespace declarations, XBRL header and first three numeric facts from the [2025 Nostrum report](https://filings.xbrl.org/2138007VWEP4MM3J8B29/2025-12-31/ESEF/GB/0/2138007VWEP4MM3J8B29-2025-12-31/reports/2138007VWEP4MM3J8B29-2025-12-31.xhtml). Layout, styles, images and all other facts were omitted. The original self-closing title is retained: HTML parsing without XHTML normalization treats the following report as title text and yields zero facts. Values, contexts, units and identifiers are unchanged.

## Ennogie interim annual-comparative regression

- `ennogie-2026-interim-excerpt.xhtml` retains four numeric facts, their unchanged contexts/units, namespace declarations and three reporting metadata facts from the [June 2026 report](https://filings.xbrl.org/549300JUGBT2EH17X827/2026-06-30/ESEF/DK/0/Ennogie_Solar_Group-2026-06-30-1-en/reports/Ennogie%20Solar%20Group-2026-06-30-1-en.xhtml), downloaded 2026-09-27.
- The report states `Interim report (6 months)`; its January–June duration and June instant coexist with a January–December comparative and December instant.
- Layout, all other facts and encoded media are omitted; the XHTML body/header wrappers and title are minimal replacements.
- Original SHA-256: `a02907d00f4caba1e5051a5f06761fa13f0d304ec4905d27db9d0cb38218c3b4` (15,682,921 bytes).
- Excerpt SHA-256: `443be29cd58b1c7b2c45656283e1201a783e088d23f1e06cf9efe9f8b63c58e8` (3,524 bytes).
- Importer orchestration tests adapt the existing Izostal rendering cassette to each index identity and period and add one explicit annual flow; the source cassette itself is unchanged.

- `ennogie-2025-annual-excerpt.xhtml` retains two unchanged numeric facts, their contexts/units and namespace declarations from the [2025 annual report](https://filings.xbrl.org/549300JUGBT2EH17X827/2025-12-31/ESEF/DK/0/EnnogieSolarGroup-2025-12-31-en/reports/EnnogieSolarGroup-2025-12-31-en.xhtml), downloaded 2026-09-27; layout and all other facts are omitted, with minimal XHTML wrappers.
- Annual original SHA-256: `2b2802e87aa09436ce8a8a897385a930ce9dee5293c6adc2461c229ba48f6cf9` (37,153,490 bytes); excerpt SHA-256: `7f1a66cceb6af903701187baabcbe58608de09e0b83f47164a24db71ca86dd08` (1,880 bytes).

## Teixeira Duarte 2024 retrieval-size regression

- `teixeira-duarte-2024-envelope.xhtml.gz` is the complete retained XHTML extraction envelope from the official [2024 report](https://www.cmvm.pt/PInstitucional/EsefViewer?Input=2D87F631793517C327F1B0A7CBC8322C2CCB3E58A5EFC1C4CC4701D58B0E3064), captured before this fix.
- The existing capture removed embedded binary assets; no text, nodes, attributes, or whitespace were changed for the fixture.
- Gzip SHA-256: `719b5e7c2ada03dd7e497244887aa0cd4841d88a105a68ad540c0d68a2505c0b`.
- The 28,900,183-character envelope exceeds the 16 Mi-character retrieval ceiling because of repeated layout declarations. Conservative style compaction reduces it to 16,046,571 characters without changing its normalized report text.
- Full original normalization and recovered normalization both produce 1,134,006 UTF-8 bytes, SHA-256 `6f653a4287fa07d953873bc045eb337e426c7a382e5d73bff6eef8443d77a3d9`.
