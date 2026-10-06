# ESEF retrieval recovery

- Keep original XHTML and XBRL envelopes unchanged; compaction applies only to the retrieval copy.
- Before the existing 16 Mi-character conversion limit, oversized valid XHTML may shed a fixed set of layout declarations unused by normalization.
- Omit XHTML scripts only when directly inside the root XHTML head or body; viewer payloads are already excluded from Markdown.
- Refuse compaction when an XHTML script has another parent or contains child elements; financial facts, contexts and continuations may depend on that markup.
- Preserve all other text, elements, namespaces, table spans, all other attributes except the layout metadata below, and all remaining styles, including emphasis, alignment, visibility, color and text decoration.
- Flat numeric `rgb(...)` and `rgba(...)` values permit declaration splitting; preserve all other complex CSS attributes in full; invalid XML and output still exceeding the limit retain the existing empty-by-design result.
- On `span` and `div`, unused nonempty class values become one neutral class; spans with such classes may clear their redundant id value. Preserve class-driven lists, math, footnotes, line blocks and code-language conventions.
- Keep nonempty class metadata: the Markdown reader uses its presence to preserve paragraph boundaries. Preserve anchor ids, classless-span ids, namespaced attributes and all other elements.
- Never return a text prefix or raise the conversion or retained-envelope limits.
- Retained JSON contributes only unqualified IFRS explanatory notes with matching issuer and report-period contexts; validate the envelope with the existing financial parser first.
- JSON notes are excerpts, never a reconstruction of the full visual report; context dates filter notes but never become invented source wording.
- Convert the complete selected markup within the existing limit; valid reports without eligible notes settle empty, while malformed envelopes and failed conversions retain retries.
- Root-level note headings never replace the HTML or body container; preserve the sibling prose and table structure.
- Readable ESEF reports and current SEC documents keep their existing normalization generation.
- The finite recovery cohort is captured inline or JSON ESEF documents with empty content, normalization version below 5, and fewer than five failed attempts.
- The existing backfill replays their retained envelopes, replaces only derived text, clears stale chunks transactionally, and stamps ESEF generation 5 even when the full report still exceeds the limit.
- Retire the empty-content selection exception only after its eligible cohort is empty, current writers stamp generation 5, and no older writers remain deployed.
- An empty cohort proves the conversion was retried; it does not prove every report became readable or any downstream extraction completed.

```sql
SELECT COUNT(*) AS eligible_empty_esef_documents
FROM "Document" d
JOIN "File" f ON f."Id" = d."ContentId"
WHERE d."DocumentType" IN ('EsefAnnualReport', 'EsefReport')
  AND d."NormalizedContentVersion" < 5
  AND d."NormalizedContentAttempts" < 5
  AND f."Size" = 0
  AND d."XbrlStatus" = 1 -- Captured
  AND d."XbrlType" IN (0, 2) -- InlineIxbrl, JsonXbrl
  AND d."XbrlContentId" IS NOT NULL;
```

## Positioned prose

- A retained `DTRTextContainer` may reconstruct adjacent inline `div.t` lines before XBRL wrappers are removed; keep every other layout unchanged.
- Require at least three same-column lines with complete inline pixel coordinates, descending baselines, 4–40 pixel gaps and at most one pixel of gap variation.
- Inventory all positioned text, including nodes without the line class; keep competing baselines, tables, lists, headings, ended sentences, numeric rows and ambiguous positions separate.
- Require plain text lines with only the line marker and generated font classes; preserve XBRL, styled or semantic child elements without joining them.
- Decline stylesheets, nested positioning, coordinate overrides, explicit breaks and unsupported inline layout declarations.
- Continue only unfinished long prose into lowercase or currency-prefixed text; preserve its original child nodes with explicit line breaks inside one paragraph.
- Never cross an element wrapper, visible intervening node or page boundary; preserve every non-whitespace character and original source file.
- This conversion does not globally reopen readable reports; a finite historical repair must identify the affected retained envelopes and use the existing normalization queue.

## Stylesheet-positioned prose

- Valid XHTML with `.pf > .pc > .t` page lines may use an isolated, loader-free CSS cascade before the existing normalization steps; no fonts, images, scripts or external stylesheets are fetched.
- Limit this path to 8 Mi-characters of markup, 32 stylesheets, 1 Mi-character of stylesheet text and 20,000 active rules; refuse DTDs, processing instructions and XML nesting beyond 128 levels.
- Decode stylesheet XML entities before CSS parsing; refuse external, alternate, scoped and conditionally selected stylesheets, unsupported rules and generated text.
- Accept screen/all rules and ignore print-only rules; unknown media may only switch off text shadows or apply transparent text stroke.
- Require absolute pixel coordinates, a uniform positive scale, a left-bottom transform origin and unambiguous same-column baselines; reject transformed ancestors and unsupported positioning or visibility.
- Join at least three adjacent unfinished Latin prose lines with consistent fonts and spacing; reject bidirectional characters and ordering controls; retain original inline XBRL and harmless inline elements, words and explicit line breaks.
- Preserve tables, separate columns, short numeric cells, ended sentences, page boundaries and all uncertain layouts; unsupported reports continue through the existing normalizer unchanged.

## Inline XBRL prose continuations

- A complete, unique, forward `continuedAt` chain can reconnect an unfinished paragraph across adjacent narrative fragments before XBRL wrappers are removed.
- Require an escaped `ix:nonNumeric` fact with its declared Inline XBRL namespace, name and context; reject missing, duplicate, shared, cyclic, nested or backward continuation targets.
- Join only a trailing letter or hyphen into lowercase prose with no intervening content; retain every source character and the printed line break.
- Keep complete paragraphs, numeric boundaries, tables, lists, excluded content and uncertain chains separate; cap chains at 128 fragments and joined paragraphs at 8,000 characters.
- Both joined fragments must share the exact enclosing fact objects; keep unrelated nested fact, quotation and deletion scopes separate.
- Refuse hidden or semantically altered boundary text using bounded local CSS rules; conditional, external, malformed and unsupported styles preserve existing boundaries.
- This changes derived retrieval text only; preserve originals, tagged financial facts and existing normalization generations.
- The importer must retain the bounded stylesheet metadata in its retrieval input; this change does not rewrite original documents or reopen historical extractions.

- Mixed-font positioned lines may reserve a vertical obstacle only when all existing geometry guards pass, every child retains the same font size and its line height stays bounded. Reserve twice the effective font height on both sides; preserve the line and never join it as prose.
