# ESEF retrieval recovery

- Keep original XHTML and XBRL envelopes unchanged; compaction applies only to the retrieval copy.
- Before the existing 16 Mi-character conversion limit, oversized valid XHTML may shed a fixed set of layout declarations unused by normalization.
- Preserve text, elements, namespaces, table spans, all other attributes except the layout metadata below, and all remaining styles, including emphasis, alignment, visibility, color and text decoration.
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
