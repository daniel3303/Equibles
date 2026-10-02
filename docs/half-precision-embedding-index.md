# Half-precision embedding index

- Migration `20260930143420_AddQwenHalfPrecisionEmbeddingIndex` owns a cosine HNSW expression index for `qwen3-embedding:0.6b` embeddings with 1024 dimensions.
- Original full-precision vectors and keyword indexes remain unchanged; other models and dimensions are outside this partial index.
- Databases holding up to 10,000 indexable embeddings create the index through migrations. A completed matching index created manually is validated and adopted without rebuilding it.
- That limit keeps the build to about ten seconds within the default `maintenance_work_mem`, inside the 30-second default command timeout of `dotnet ef database update`.
- Above the limit the migration refuses to build and asks for a manual build, because a build cancelled by the command timeout leaves an invalid index that fails every later startup.
- For the manual build, execute this statement outside a transaction, then apply the migration; use an explicitly budgeted session that survives terminal disconnection:

```sql
CREATE INDEX CONCURRENTLY IF NOT EXISTS "IX_Embedding_Qwen3_Halfvec1024_Hnsw"
ON public."Embedding" USING hnsw (("Vector"::public.halfvec(1024)) public.halfvec_cosine_ops)
WITH (m = 16, ef_construction = 64)
WHERE "Model" = 'qwen3-embedding:0.6b' AND "VectorDimension" = 1024;
```

- Keep the `WITH` clause and the predicate order as written: an index built without them is refused even when its behavior is equivalent.
- Do not mark the migration applied manually. Let EF validate the index and record it after the build completes.
- Invalid indexes, different definitions, conflicting relation names, and active builds fail closed when applying; no automatic drop or repair is attempted.
- Remove an invalid leftover with `DROP INDEX CONCURRENTLY public."IX_Embedding_Qwen3_Halfvec1024_Hnsw"` once no build is running, then build again.
- The definition check compares the index with one the server builds from the same SQL on an empty temporary copy of the table, so it does not depend on the PostgreSQL version or the declared column types.
- The migration role therefore needs the `TEMPORARY` privilege on the database.
- Rolling the migration back drops the index, valid or not. Re-applying it above the limit needs the manual build again.
- Rollback refuses while a build is running, including an unfinished index whose table is locked by another session that may be building it; retry once that session ends.
- Index definitions are versioned SQL because the cast expression is not represented by the EF model snapshot.
- Queries must filter the indexed model and dimension and order by `"Vector"::halfvec(1024) <=> <query>::halfvec(1024)` to use this index; index creation alone does not change retrieval behavior.
- Run `dotnet test tests/Equibles.Migrations.IntegrationTests/Equibles.Migrations.IntegrationTests.csproj -c Release` to exercise creation, adoption, failure safety, history recording, and rollback against disposable ParadeDB.
