# Upgrade to the latest release

Pull the latest Equibles code, rebuild your containers, and let database migrations apply themselves on startup.

1. Decide if you want to upgrade now. Open [`CHANGELOG.md`](../../CHANGELOG.md) on GitHub (or in your local checkout after step 2) and read the entries between your current version and `main`. Equibles uses semantic versioning, so a major-version bump may include breaking changes — those are called out at the top of each release section.

2. From your `Equibles/` directory, pull the latest code:

   ```bash
   git pull
   ```

   If you've edited any tracked files locally (rare on a self-hosted deploy — `.env` is gitignored), commit or stash them first.

3. Rebuild and restart the stack in the background:

   ```bash
   docker compose up -d --build
   ```

   The `--build` flag tells Compose to rebuild the `web`, `mcp`, and `worker` images from the new source. The `db` image is pulled in step 4 only if its tag changed.

4. Watch the `web` container's startup log:

   ```bash
   docker compose logs -f web
   ```

   On the first run after pulling, you'll see EF Core apply any pending database migrations. Output looks like `Applying migration '20260518145124_AddSecFinancialFacts'`. First-time migrations that build large indexes (BM25 over SEC chunks, pgvector embeddings) can take several minutes; the container is **not** stuck, just busy. The 1-hour command timeout in the host code keeps it from being killed.

5. When the log settles back to normal request lines (`Request starting HTTP/1.1 GET /`), the upgrade is complete. Press `Ctrl-C` to detach from the log stream (the containers stay running).

6. Verify in your browser. Open `http://localhost:8080` and look at the footer or the **Status** page — the version banner should match the latest release tag. If the web portal had been showing an update-available banner before, it now disappears.

If a migration fails or a container crashes after the upgrade, roll back by checking out the previous tag and running the same `docker compose up -d --build` command. Example:

```bash
git checkout v0.4.2     # whatever tag you were on
docker compose up -d --build
```

The database stays compatible with older versions as long as you don't re-apply incompatible migrations manually.

To skip the GitHub-Releases update banner entirely (useful on air-gapped deploys), set `CHECK_FOR_UPDATES=false` in your `.env` and recreate the web container.
