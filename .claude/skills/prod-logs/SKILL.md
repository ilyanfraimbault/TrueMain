---
name: prod-logs
description: Fetch the latest production error logs and crash reports — the same data as the admin portal's Logs tab (admin.truemain.lol) — by logging into the admin proxy and querying /api/ops/logs and /api/ops/crashes. Use when the user asks what's failing in prod, "mes dernières erreurs en prod", to check prod logs/crashes, or to investigate a production incident.
---

# Prod logs — query the admin ops endpoints

Goal: pull the exact rows the admin portal's Logs tab shows (Mongo `logs` / `crashes` collections, served by `GET /ops/logs|crashes` on the API) without opening a browser. The API is not exposed publicly; the only remote path is the admin app's session-gated proxy at `https://admin.truemain.lol/api/ops/*`.

## 1. Authenticate (once per session)

Credentials are `ADMIN_USERNAME` / `ADMIN_PASSWORD` in the **main repo root** `.env` (in a worktree, resolve it via `git worktree list`):

```bash
ENV_FILE="$(git worktree list | head -1 | awk '{print $1}')/.env"
JAR=/tmp/truemain-admin-cookies.txt
curl -sf -c "$JAR" -X POST https://admin.truemain.lol/api/auth/login \
  -H 'Content-Type: application/json' \
  -d "$(awk -F= '/^ADMIN_USERNAME=/{u=$2} /^ADMIN_PASSWORD=/{p=$2} END{printf "{\"username\":\"%s\",\"password\":\"%s\"}", u, p}' "$ENV_FILE")"
```

Success returns `{"ok":true}` and stores the httpOnly session cookie in the jar.

**Throttle warning:** login is limited to 5 attempts per minute per IP. On a 401, do NOT retry in a loop — the prod credentials may differ from the local `.env` (prod runs its own unversioned `.env` on the VPS). Report it and ask the user for the prod credentials instead. Never print the password.

## 2. Fetch logs

```bash
curl -sf -b "$JAR" 'https://admin.truemain.lol/api/ops/logs?level=Error&pageSize=50'
```

Query params (all optional, omit = no filter):

- `level` — **minimum** severity threshold: `Trace|Debug|Information|Warning|Error|Critical` (e.g. `Warning` includes Error and Critical). Default for "my latest errors": `Error`.
- `since` — ISO datetime lower bound (e.g. `2026-07-15T00:00:00Z`).
- `category` — case-insensitive **prefix** match on the logger namespace (e.g. `Ingestor.Processes.MatchIngestionProcess`).
- `search` — case-insensitive substring over message **and** exception text (collection scan — combine with `since` when possible).
- `eventType` — exact ops-event name; the response's `eventTypes` lists the catalog.
- `page` (1-based), `pageSize` (clamped to [1, 200], default 50).

Response: `{ entries, total, page, pageSize, eventTypes }`. Each entry: `id, timestampUtc, level, category, message, exception (full formatted stack trace or null), processName (Api|Ingestor), host, eventType`. Sorted newest first.

## 3. Fetch crashes (the portal's "Crashes" tab)

```bash
curl -sf -b "$JAR" 'https://admin.truemain.lol/api/ops/crashes?pageSize=25'
```

Params: `since`, `process` (`Api|Ingestor`), `source`, `search` (substring over message + stackTrace), `page`, `pageSize` (clamped to [1, 100]). Rows carry `stackTrace`, `innerExceptions[]`, environment/memory snapshot, `exitCode`, `recentLogTail[]`. Note: OOM kills / SIGKILL show up here as `UncleanShutdown` (detected at next boot), not in the logs collection.

## 4. Report — synthesize, don't dump

Prod errors are highly repetitive (one root cause → hundreds of near-identical rows). Do not paste raw JSON or every row:

1. Group entries by `category` + normalized message shape (strip PUUIDs/match ids) and report each group once with its count and time range.
2. For each group, show one representative full `exception` (that's where the root cause lives — e.g. the HTTP status in a `HttpRequestException`).
3. State `total` vs what one page shows; page or narrow with `since`/`category` if the picture is incomplete.
4. Cross-check known incident signatures before diagnosing something new: 401s from Riot = expired/rotated dev API key; 429 = rate limit; Mongo/disk issues have precedents in memory and past issues.

Clean up the cookie jar (`rm -f "$JAR"`) when done.
