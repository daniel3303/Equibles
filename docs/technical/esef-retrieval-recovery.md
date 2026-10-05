# ESEF retrieval recovery

- Keep original XHTML and XBRL envelopes unchanged; compaction applies only to the retrieval copy.
- Before the existing 16 Mi-character conversion limit, oversized valid XHTML may shed a fixed set of layout declarations unused by normalization.
- Preserve text, elements, namespaces, table spans, all other attributes, and all remaining styles, including emphasis, alignment, visibility, color and text decoration.
- Preserve complex CSS attributes in full; invalid XML and output still exceeding the limit retain the existing empty-by-design result.
- Never return a text prefix or raise the conversion or retained-envelope limits.
- Readable ESEF reports and current SEC documents keep their existing normalization generation.
- The finite recovery cohort is captured inline ESEF documents with empty content, normalization version below 2, and fewer than five failed attempts.
- The existing backfill replays their retained envelopes, replaces only derived text, clears stale chunks transactionally, and stamps ESEF generation 2 even when the full report still exceeds the limit.
- Retire the empty-content selection exception only after its eligible cohort is empty, current writers stamp generation 2, and no older writers remain deployed.
- An empty cohort proves the conversion was retried; it does not prove every report became readable or any downstream extraction completed.

```sql
SELECT COUNT(*) AS eligible_empty_esef_documents
FROM "Document" d
JOIN "File" f ON f."Id" = d."ContentId"
WHERE d."DocumentType" IN ('EsefAnnualReport', 'EsefReport')
  AND d."NormalizedContentVersion" < 2
  AND d."NormalizedContentAttempts" < 5
  AND f."Size" = 0
  AND d."XbrlStatus" = 1 -- Captured
  AND d."XbrlType" = 0 -- InlineIxbrl
  AND d."XbrlContentId" IS NOT NULL;
```
