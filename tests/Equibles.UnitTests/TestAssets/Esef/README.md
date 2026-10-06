# European filing index captures

## B2 Impact 2025 stylesheet-positioned borrowing paragraphs

- `b2-impact-2025-styled-borrowings.xhtml.gz` contains complete page elements `pf8d` and `pf99` (printed pages 141 and 153) and all six stylesheets from the retained [2025 annual report](https://api3.oslo.oslobors.no/v1/newsreader/attachment?messageId=672002&attachmentId=324636), read on 2026-10-06.
- Embedded font/image payloads and the other pages were omitted; the selected pages keep their text and coordinates, with minimal XHTML wrappers and XML serialization of equivalent namespace and empty-element syntax.
- Original: 15,331,068 bytes, SHA-256 `0c47d8dc2742ad5a5da6144e36e51a3aa821760fef6d20e345a6cbc30573c3ba`.
- Uncompressed fixture: 409,865 bytes, SHA-256 `229d920016607503a1698ae6ae4db10f8b3b145a29bfb373b055018a8ac96a6a`.
- Notes 24 and 31 exercise adjacent prose lines, inline XBRL, kerning spans, stylesheet XML entities and an unchanged positioned table container.
- The source names B2Kapital Holding S.à r.l. as borrower and B2 Impact ASA as guarantor; joining their original paragraph does not change those roles.

## Index captures

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

## Teixeira Duarte retrieval-size regressions

- `teixeira-duarte-2024-envelope.xhtml.gz` is the complete retained XHTML extraction envelope from the official [2024 report](https://www.cmvm.pt/PInstitucional/EsefViewer?Input=2D87F631793517C327F1B0A7CBC8322C2CCB3E58A5EFC1C4CC4701D58B0E3064), captured before this fix.
- The existing capture removed embedded binary assets; no text, nodes, attributes, or whitespace were changed for the fixture.
- Gzip SHA-256: `719b5e7c2ada03dd7e497244887aa0cd4841d88a105a68ad540c0d68a2505c0b`.
- The 28,900,183-character envelope exceeds the 16 Mi-character retrieval ceiling because of repeated layout declarations. Conservative style compaction brings it below the ceiling without changing its normalized report text.
- Full original normalization and recovered normalization both produce 1,134,006 UTF-8 bytes, SHA-256 `6f653a4287fa07d953873bc045eb337e426c7a382e5d73bff6eef8443d77a3d9`.
- `teixeira-duarte-2025-envelope.xhtml.gz` is the complete retained extraction envelope from the official [2025 report](https://www.cmvm.pt/PInstitucional/EsefViewer?Input=24CA145C2C924C1A5E8329A11A98F54663B3C16899747C02B3AA47E49B34E4B8); the fixture is unchanged, with the same existing binary-asset compaction as the 2024 envelope.
- Its gzip SHA-256 is `3e6f41bdd07a21cf8bda720f6d2c643fca15b1bd8544df70fafc130771c97d5f`; the 33,501,816-character envelope also needs unused margin, padding, border and vertical-alignment declarations removed to fit.
- Full 2025 normalization produces 1,278,650 UTF-8 bytes, SHA-256 `2f5a2f4d5eab330fd42b038982f2a9ed0d4d2cde891a4166d709801a4df418d4`; recovered normalization must match exactly.

## JD Sports 2026 numeric color functions

- `jd-sports-2026-envelope.xhtml.gz` is the complete retained XHTML extraction envelope from the [official FCA report package](https://data.fca.org.uk/artefacts/NSM/DirectUpload/NI-000145262/NI-000145262_213800HROV6Y9MUU8375-2026-01-31.zip).
- The retained envelope already omits embedded binary assets; the fixture changes no bytes, text, nodes, attributes or whitespace.
- Gzip SHA-256: `3f8eacf563a80d735f419540f725a19f9e753775340edf5c5ee22383ae7d3c81`.
- Repeated unused layout declarations share attributes with flat numeric `rgba(...)` functions; preserving those entire attributes leaves 20,768,126 characters above the retrieval limit.
- Full original normalization produces 929,052 UTF-8 bytes, SHA-256 `2e9284d51b74fa650113d1a5ba215d8e2c0fc3ce9fd624d59484d31a21c6fe9d`; recovered normalization must match exactly.

## BFF 2025 retrieval metadata

- `bff-2025-envelope.xhtml.gz` is the complete retained XHTML envelope from the [official 1INFO report package](https://www.1info.it/PORTALE1INFO/Pdf/Pdf?pdf=167713_oneinfo.zip&data=2025&filetype=documenti&titolo=&download=1); no fixture bytes were changed.
- Its gzip SHA-256 is `76611d4b227a25cd6fd3a5329903d03fcb52bcbdc8e24161670a47a1f96149fa`; the 34,672,821-character envelope retains the capture's existing binary-asset removal.
- Repeated span/div layout classes and span ids dominate its size. Removing class metadata changes Markdown paragraph boundaries, so the compact retrieval copy preserves a neutral nonempty class.
- Full original normalization produces 2,048,711 UTF-8 bytes, SHA-256 `c599cd1356a8f79910cfca43f03dd28d085bf77bd144421d17c36abf703ba474`; recovered text must match exactly, including whitespace.

## DFDS interim report with trailing twelve-month figures

- `dfds-2026-interim-excerpt.xhtml` retains three numeric facts, three reporting metadata facts, and their unchanged contexts, units, and namespace bindings from the [June 2026 interim report](https://filings.xbrl.org/549300JZVW1Y1UZ5UK38/2026-06-30/ESEF/DK/0/DFDS-2026-06-30-1-en/reports/DFDS-2026-06-30-1-en.xhtml), captured from the retained original on 2026-10-05.
- The report declares January–June 2026 while reporting trailing twelve-month IFRS figures through June; those figures cannot override the declared reporting period.
- Layout, media and other facts are omitted; body/header wrappers and the title are minimal replacements.
- Original retained source: 6249827 bytes, SHA-256 `8e69e741597b9d52e219ec142158fc6e0ea1fd46bc05c0d340c6bc3dd6ac34fe`.
- Excerpt: 3112 bytes, SHA-256 `3709fc83288d8d271848b7890acfefc18e3f45aa8f8101541af36f265cb7613c`.

## Better Collective JSON borrowing disclosures

- `better-collective-2025-json-notes-excerpt.json` retains the complete unchanged `documentInfo` and five fact objects from the [2025 report](https://filings.xbrl.org/2549001EPXH6NK7I2R78/2025-12-31/ESEF/DK/0/bettercollective-2025-12-31-en.json), read from the retained original on 2026-10-05.
- Fact `f1__s9__7__242-1` is the complete borrowing disclosure; four numeric facts retain current-period issuer and annual evidence. Other facts were omitted and JSON formatting was expanded; fact values and dimensions were not edited.
- Original decompressed JSON: 5,903,989 bytes, SHA-256 `9baaf1134344c2ee7eda38c77e790857001b49b994c8068e20fd0919b79662a0`.
- Excerpt: 50,622 bytes, SHA-256 `cfb639531bbc0c9aae2961430ca603860c8bca35256b5d6e33f300049ce59d16`.
- Tagged narratives provide excerpts, not the complete visual report; numeric context dates select eligible notes and never supply invented wording in the text.

## Syncona 2026 root-level note heading

- `syncona-2026-payables-note.html.gz` is the unchanged value of `TagsThatMustBeAppliedIfCorrespondingInformationIsPresentInAReport_Label_0047` from the retained [2026 Syncona Limited xBRL-JSON report](https://filings.xbrl.org/213800X8MBI5VQITLW60/2026-03-31/ESEF/GB/0/213800X8MBI5VQITLW60-2026-03-31-T01.json) (LEI `213800X8MBI5VQITLW60`), read on 2026-10-06.
- The fragment retains the uppercase root span and complete accrued-expenses/payables table; no markup or whitespace was edited.
- Fragment SHA-256: `f2b59ecd48fec0bc21f003bd75c6139165c20cbae52b5303d5dd54f5fad2043f` (3533 bytes).
- A root heading must never replace the document body and discard its table.
- Original decompressed JSON SHA-256: `289feea2ac743aea37d0a34d629478f3cbe37786037a09a197a857eb1b513323` (1338544 bytes).
- The fixture is gzip-compressed without changing the captured whitespace; gzip SHA-256: `e803fdb6d57672ca58136d7d75dd866e0d025df7f5f524e78714faf735846400`.

## Hostelworld 2025 positioned borrowing paragraph

- `hostelworld-2025-borrowings-page.xhtml.gz` contains the complete unchanged `DTRTextContainer` element for printed page 203 from the retained [2025 annual report](https://data.fca.org.uk/artefacts/NSM/DirectUpload/NI-000142085/NI-000142085_213800OC94PF2D675H41-2025-12-31.zip), read on 2026-10-06.
- The fixture omits the surrounding report; the selected element, coordinates, inline XBRL wrappers, tables and whitespace are unchanged.
- Retained extraction envelope: 3,232,369 UTF-8 bytes, SHA-256 `48f04fb34620d028e426bf9833879c607e34533e39f0a6942144c740a6a3dae8`.
- Uncompressed page: 21,443 bytes, SHA-256 `af4f7e71dfc033aee4dce0cdd925c8dfc65cfb4bbf03504c819f4ce3bf95fda1`.
- Its borrowing paragraph occupies four same-column lines at bottom coordinates 166, 148, 129 and 111 pixels; a separate table precedes it.
- The test exercises complete normalization and conversion, preserving that table while removing artificial paragraph breaks inside the loan disclosure.

## Awilco 2025 mixed-font table cells

- `awilco-2025-guarantees-page.xhtml.gz` retains complete printed page 59 (`pf3b`), its ancestor attributes and all retained stylesheets from the [official 2025 annual report](https://api3.oslo.oslobors.no/v1/newsreader/attachment?messageId=670628&attachmentId=323333).
- The retained XHTML envelope removes embedded binary assets; the fixture omits other pages and reporting contexts, without editing page text, geometry or styles.
- Original: 21,795,024 bytes, SHA-256 `368d60863a7d5d5b689307514d794f2dc2654d83cd683e723047fded7d1255c6`.
- Derived envelope: 1,400,372 bytes, SHA-256 `42ee7ec800f8e61fc8d9c5e80f7833e04ce52f4878bcf0956afc0b9bf537555e`.
- Uncompressed fixture: 166,322 bytes, SHA-256 `3af8d215634d2504b8042b379b220b06be65729ea1fd2893ace8439dda87ebea`.
- Four numeric cells use a different inline font and line height. They remain separate, bounded obstacles while the three-line lessees paragraph retains both named subsidiaries.

## Glaston 2025 liquidity disclosure continuation

- `glaston-2025-liquidity-continuation.xhtml.gz` retains the complete `f0__s7__6__114` narrative and both referenced continuations from the [2025 annual report](https://www.oam.fi/cns-web/oam/viewAttachment.action?messageAttachmentId=340873), read on 2026-10-06.
- The three complete stylesheets follow the retained extraction envelope, with embedded binary payloads removed; other report content and media are omitted. XML serialization preserves equivalent namespace and empty-element syntax.
- Original: 36,428,122 bytes, SHA-256 `3389c81ae6d0cadbd557ca3958c99014fba8827a7958a9018d3769d6ab8935be`.
- Uncompressed fixture: 1673240 bytes, SHA-256 `77ab8283722c187202f68144ee50cef099fe09a5d474a77408258b41aa88c4d2`.
- The issuer sentence ends in `con-`; the next linked fragment continues it with `sists` and the facility amounts. The following complete paragraph and financial tables remain separate.
