# Ask your AI assistant to search a company's SEC filings

Equibles indexes the full text of every SEC filing it scrapes — annual reports (10-K), quarterly reports (10-Q), and current reports (8-K) — and exposes them through the MCP server, so you can ask your AI assistant to find and read what a company actually said in its own filings instead of relying on the model's training data. This is the capability behind questions like "What does Apple's latest 10-K say about revenue growth?"

## Before you start

- Connect an AI assistant to the MCP server first — see [Connect an AI assistant](tutorial-connect-ai-assistant.md).
- Let the worker run for a while after first startup so the SEC scraper has imported and converted the filings. It needs no API key — the data comes from SEC EDGAR.
- Full-text keyword search works out of the box. The embedding profile adds meaning-based (semantic) matching on top, which helps when your wording differs from the filing's — see [Enable semantic search over SEC filings](how-to-enable-embedding-search.md).

## Ask about a company's filings

Name a company and ask what its filings say:

- "What does Apple's latest 10-K say about revenue growth?"
- "Search Microsoft's filings for commentary on cloud margins."
- "What risk factors does NVDA call out in its most recent annual report?"

The assistant searches that company's filings with the `SearchCompanyDocuments` tool and replies with the most relevant excerpts, each labelled with the document type and filing date. If you don't know which company holds the answer, ask a broader question — for example, "Which companies discuss tariff exposure in their recent filings?" — and the assistant uses `SearchDocuments` to search across every company at once.

## Drill into one filing

To work inside a single filing rather than across many:

- "List Apple's recent SEC filings." — the assistant calls `ListCompanyDocuments` and returns each filing's type, filing date, and reporting period.
- "Search that 10-K for what they say about supply-chain risk." — the assistant uses `SearchDocument` to find the relevant passages, `SearchDocumentKeyword` to match an exact word or phrase, and `ReadDocumentLines` to read a passage in full.

You can narrow any of these by document type (10-K, 10-Q, or 8-K) or by filing-date range — for example, "only 10-Qs filed in 2024."

## What you should see

Short excerpts quoted from the company's own filings, each tagged with the document type and filing date, followed by the assistant's summary. The text is exactly what the company filed with the SEC — Equibles never rewrites it.

If the reply says it can't find anything, the most likely reasons are that the scraper hasn't imported that company's filings yet, or you're searching a company with few filings on record. A large, frequently-filing company such as AAPL or MSFT is the best place to confirm the data is flowing.

To search the same filings from the browser instead of an AI assistant, see [Search for stocks, filings, institutions, and more](how-to-search.md).
