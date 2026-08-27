# Enable semantic search over SEC filings

Apply the embedding Compose override so the MCP `SearchDocuments` / `SearchDocument` tools add meaning-based ("semantic") ranking to SEC filing search — e.g. find passages about supply-chain risk in Apple's 10-K even when the words do not match. Without embeddings, semantic-mode requests retain ranked BM25 lexical search, and exact mode still performs literal matching.

The override adds a local Ollama runtime and the Qwen3-Embedding-0.6B embedding model. Expect a one-time ~2.6 GB download (~2 GB Ollama image + ~640 MB model) and around 3 GB of additional RAM use while running.

1. If the stack is currently running, stop it first so Docker Compose can apply the override cleanly:

   ```bash
   docker compose down --remove-orphans
   ```

   This stops the containers without deleting your database and removes any retired profile worker left by an older release.

2. Bring the stack back up with the embedding override:

   ```bash
   docker compose -f docker-compose.yml -f docker-compose.embedding.yml up -d
   ```

   The first run pulls the Ollama image and starts an `embedding-pull` init container that downloads Qwen3-Embedding-0.6B (~640 MB). The existing `worker` waits for that download before it starts with embeddings enabled. Watch progress with `docker compose -f docker-compose.yml -f docker-compose.embedding.yml logs -f embedding embedding-pull`.

   Keep using both `-f` arguments for later starts, stops, logs, and upgrades so Compose continues to include the optional services.

3. Wait for `embedding-pull` to exit successfully — its log ends with something like `success`. After that the single `worker` starts generating embeddings for every SEC document chunk in the database. The first backfill can take an hour or more, depending on how many filings you've ingested.

4. Check progress on `http://localhost:8080/status`. The **SEC documents** count holds steady (embeddings don't add documents) but the **chunks with embeddings** number climbs from zero toward the total chunk count.

5. Try a semantic-search prompt against your AI assistant. Once the first batch of chunks is embedded (a few minutes), this should return real results:

   *"Use the Equibles SearchDocuments tool to find passages in Apple's most recent 10-K about supply-chain risk. Summarise the top three matches."*

   If `SearchDocuments` returns empty results even though the worker is running, confirm `Embedding__BaseUrl=http://embedding:11434` and `Embedding__ModelName=qwen3-embedding:0.6b` are set in the `mcp` container's environment — `docker compose -f docker-compose.yml -f docker-compose.embedding.yml config mcp` should show them.

To turn embeddings back off, run `docker compose -f docker-compose.yml -f docker-compose.embedding.yml down` followed by `docker compose up -d`. The embeddings already in the database stay searchable; only new chunks stop getting embedded.

For a deeper look at how the embedding pipeline works, see [Operations → Embedding override](../technical/operations.md#embedding-override-opt-in).
