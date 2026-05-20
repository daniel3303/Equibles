# Enable semantic search over SEC filings

Turn on the embedding profile so the MCP `SearchDocuments` / `SearchCompanyDocuments` tools can do meaning-based ("semantic") search across SEC filings — e.g. find every passage about supply-chain risk in Apple's 10-K, even when the words don't match. Without this, only exact-keyword search works.

The profile adds a local Ollama runtime and the BGE-M3 embedding model. Expect a one-time ~3.2 GB download (~2 GB Ollama image + ~1.2 GB model) and around 4 GB of additional RAM use while running.

1. If the stack is currently running, stop it first so Docker Compose can apply the new profile cleanly:

   ```bash
   docker compose down
   ```

   This stops the containers without deleting your database.

2. Bring the stack back up with the `embedding` profile active:

   ```bash
   docker compose --profile embedding up -d
   ```

   The first run pulls the Ollama image, starts an `embedding-pull` init container that downloads BGE-M3 (~1.2 GB), then starts a `worker-embedding` service in place of the default `worker`. Watch progress with `docker compose --profile embedding logs -f embedding embedding-pull`.

3. Wait for `embedding-pull` to exit successfully — its log ends with something like `success`. After that the `embedding` container's healthcheck flips to healthy and `worker-embedding` starts generating embeddings for every SEC document chunk in the database. The first backfill can take an hour or more, depending on how many filings you've ingested.

4. Check progress on `http://localhost:8080/status`. The **SEC documents** count holds steady (embeddings don't add documents) but the **chunks with embeddings** number climbs from zero toward the total chunk count.

5. Try a semantic-search prompt against your AI assistant. Once the first batch of chunks is embedded (a few minutes), this should return real results:

   *"Use the Equibles SearchDocuments tool to find passages in Apple's most recent 10-K about supply-chain risk. Summarise the top three matches."*

   If `SearchDocuments` returns empty results even though the worker is running, the embedding endpoint isn't configured for the MCP server. Confirm `Embedding__BaseUrl=http://embedding:11434` and `Embedding__ModelName=bge-m3` are set in the `mcp` container's environment — `docker compose --profile embedding config mcp` should show them.

To turn embeddings back off, run `docker compose --profile embedding down` followed by `docker compose up -d` (no profile flag). The embeddings already in the database stay searchable; only new chunks stop getting embedded.

For a deeper look at how the embedding pipeline works, see [Operations → Embedding profile](../technical/operations.md#embedding-profile-opt-in).
