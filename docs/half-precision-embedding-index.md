# Half-precision embedding index

- Migration `20260930143420_AddQwenHalfPrecisionEmbeddingIndex` owns a cosine HNSW expression index for `qwen3-embedding:0.6b` embeddings with 1024 dimensions.
- Original full-precision vectors and keyword indexes remain unchanged; other models and dimensions are outside this partial index.
- Fresh databases create the index through migrations. A completed matching index created manually is validated and adopted without rebuilding it.
- On large existing databases, execute the `CreateSql` statement from `Infrastructure/QwenHalfvecIndex20260930.cs` outside a transaction before deploying the migration; use an explicitly budgeted session that survives terminal disconnection.
- Do not mark the migration applied manually. Let EF validate the index and record it after the build completes.
- Invalid indexes, different definitions, conflicting relation names, and active builds fail closed; no automatic drop or repair is attempted.
- Index definitions are versioned SQL because the cast expression is not represented by the EF model snapshot.
- Queries must filter the indexed model and dimension and order by `"Vector"::halfvec(1024) <=> <query>::halfvec(1024)` to use this index; index creation alone does not change retrieval behavior.
- Run `dotnet test tests/Equibles.Migrations.IntegrationTests/Equibles.Migrations.IntegrationTests.csproj -c Release` to exercise creation, adoption, failure safety, history recording, and rollback against disposable ParadeDB.
