# Complete 13F submission

- Source: https://www.sec.gov/Archives/edgar/data/1053906/0001053906-23-000008.txt
- Captured: 2026-09-25; original bytes retained without edits.
- SHA-256: `adfdc0f6a85a5edd3bdb63b820a31a8e87b5d41f0b6ff1cf7b67c3a29015afaa`.
- Covers the SEC SGML envelope, XML declarations, namespaces, two reported CUSIPs, and distinct other-manager lists.
- Declared information-table rows: 119; declared value: 3,226,843 in the source's filed units.
- The replay test runs offline; source download belongs to fixture maintenance.

# Complete submission with inconsistent cover totals

- Source: https://www.sec.gov/Archives/edgar/data/1076598/0000940394-21-000993.txt
- Captured: 2026-09-25; original bytes retained without edits.
- SHA-256: `9b94855315b0017b01ffeaf54ae3a730c78254761f9de81ac9f053475ee64c1d`.
- The cover declares 2,925 rows and 67,435,365; its information table contains 2,924 rows totalling 67,439,348 in filed units.
- Reuse the two safe artifact filenames for standalone XML retrieval while retaining the strict complete-submission acceptance rule.

# Source-confirmed zero positions

- Source: https://www.sec.gov/Archives/edgar/data/1634047/0001172661-23-003928.txt
- Captured: 2026-09-26; original bytes retained without edits.
- SHA-256: `0dc9812340320699e2256df912b4b67b14c967edd917f9727b9088f91ca8a9ce`.
- The manager explicitly reports that all discretionary authority transferred on July 15, 2023 and this is its final filing.
- Declared row count is one and value is zero; the sole placeholder row explicitly reports zero shares, value, and every voting quantity.
- Completion uses these structured source quantities, verified envelope declarations, and an empty retained quarter; no name or identifier pattern classifies the placeholder.

# Complete original whose standalone table is unavailable

- Source: https://www.sec.gov/Archives/edgar/data/1162777/000095012320012501/0000950123-20-012501.txt
- Captured: 2026-09-26; original bytes retained without edits.
- SHA-256: `ab618919e1d3578dcdc2b9a5f97095de0334e72d8c0a0fbd4b0ec9136e93ac2f`.
- The cover declares 8 rows and 4,359,220; the complete information table has 10 rows totalling 4,406,898 in filed units.
- The standalone `1972.xml` returns 404; retain the complete original's positions without marking its cover totals verified.
- An available standalone table keeps precedence; amendments and zero quantities cannot use this fallback.

# Source-confirmed zero restatement

- Source: https://www.sec.gov/Archives/edgar/data/1729347/000199937126020307/0001999371-26-020307.txt
- Captured: 2026-09-26; original bytes retained without edits.
- SHA-256: `16b93695ae364d84862df17594d5d141ae3e2e4857fe96acd76fb3ab9a4c7729`.
- The restatement corrects a filing submitted under the wrong manager; the complete submission verifies one row with explicit zero shares, value, and votes.
- Its empty book must replace the original without falling back to the prior quarter or treating missing quantities as zero.
