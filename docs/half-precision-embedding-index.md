# Half-precision embedding index

- Migration `20260930143420_AddQwenHalfPrecisionEmbeddingIndex` owns a cosine HNSW expression index for `qwen3-embedding:0.6b` embeddings with 1024 dimensions.
- Original full-precision vectors and keyword indexes remain unchanged; other models and dimensions are outside this partial index.
- Databases holding up to 100,000 indexable embeddings create the index through migrations. A completed matching index created manually is validated and adopted without rebuilding it.
- Above that limit the migration refuses to build and asks for a manual build, because the build would outlive the migration command timeout and a cancelled build leaves an invalid index.
- For the manual build, execute the `CreateSql` statement from `Infrastructure/QwenHalfvecIndex20260930.cs` outside a transaction, then apply the migration; use an explicitly budgeted session that survives terminal disconnection.
- Do not mark the migration applied manually. Let EF validate the index and record it after the build completes.
- Invalid indexes, different definitions, conflicting relation names, and active builds fail closed when applying; no automatic drop or repair is attempted.
- Remove an invalid leftover with `DROP INDEX CONCURRENTLY public."IX_Embedding_Qwen3_Halfvec1024_Hnsw"` once no build is running, then build again.
- The definition check compares the index with one the server builds from the same SQL on an empty temporary copy of the table, so it does not depend on the PostgreSQL version or the declared column types.
- Rolling the migration back drops the index, valid or not, unless a build is running. Re-applying it on a large database needs the manual build again.
- Index definitions are versioned SQL because the cast expression is not represented by the EF model snapshot.
- Queries must filter the indexed model and dimension and order by `"Vector"::halfvec(1024) <=> <query>::halfvec(1024)` to use this index; index creation alone does not change retrieval behavior.
- Run `dotnet test tests/Equibles.Migrations.IntegrationTests/Equibles.Migrations.IntegrationTests.csproj -c Release` to exercise creation, adoption, failure safety, history recording, and rollback against disposable ParadeDB.
