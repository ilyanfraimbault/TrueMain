# Production environment

Production runs the released images (`:latest`, tagged with the release
version), which are built and published when a GitHub Release is published
(see `.github/workflows/deploy-prod.yml` and `docs/ci.md`). The stack is
defined by `compose.prod.yaml`.

Design goals:

- **Tracks releases** — every published release rebuilds the images and, once
  the Hostinger credentials are configured, redeploys the VPS automatically.
- **Production Riot API key** — never the preprod key. PUUIDs are encrypted
  per API app, so the key and the database form an inseparable pair. Replacing
  it is a destructive operation with its own runbook:
  [`docs/riot-key-switch.md`](riot-key-switch.md).
- **Full-volume ingestion** — `compose.prod.yaml` runs the largest data-diet
  knobs (see the table in `docs/preprod.md`); most are explicit overrides now
  (#811), a few still fall back to the options classes' code defaults.

## Updating prod to the latest release

### Automatic (Hostinger Docker Manager API)

The `deploy` job of the `rollout` workflow called by `deploy-prod.yml`
redeploys the `truemain` Docker Manager project right after the release images
are published and the migrations applied, using the
official `hostinger/deploy-on-vps` action (a pure API call — no SSH material in
CI). It needs three pieces of repository configuration:

| Kind     | Name                    | Value                                                        |
| -------- | ----------------------- | ------------------------------------------------------------ |
| variable | `HOSTINGER_PROD_VM_ID`  | the prod VPS id from the Hostinger API                       |
| secret   | `HOSTINGER_PROD_API_KEY`| API token from the **prod** Hostinger account (hPanel → Account → API) |
| secret   | `PROD_ENV_FILE`         | newline-separated `KEY=value` pairs mirroring the VPS `.env` |

Prod and preprod are on **separate Hostinger accounts**, so an API token is
account-scoped: prod uses `HOSTINGER_PROD_API_KEY` and preprod uses
`HOSTINGER_PREPROD_API_KEY`. Each token sees only its own account's VM, so
using one against the other's `vm_id` fails with `403 [VPS:2000] Unauthorized`
— which is exactly what happened on 2026-09-08, when both secrets were
rewritten one second apart and preprod stopped deploying while prod kept
working. The preprod secret used to be called `HOSTINGER_API_KEY`; it was
renamed so the pair reads as a pair and neither can be mistaken for "the"
Hostinger key.

The action points Docker Manager at `compose.prod.yaml` at the released commit,
so the project on the VPS always matches the release. Keep `PROD_ENV_FILE` in
sync when a new variable is added to the compose file.

These are not checked by the deploy itself but by a `preflight` job that
every other job in the workflow depends on, and which **fails the run** — no
green skip — when any of them (plus the two SSH secrets below) is missing. The
distinction matters because the migration runs *before* the deploy:
checking the deploy configuration at deploy time meant an empty `PROD_ENV_FILE`
skipped the image roll while the migrations had already been applied, leaving
prod on the old binary against the new schema — precisely the mismatch
`docs/production-migrations.md` exists to prevent. Checking up front means an
incomplete configuration stops the release before anything on the VPS moves.

`PROD_ENV_FILE` must be non-empty in particular because the action overwrites
the project `.env` on every run, so deploying with an empty secret would wipe
the prod `.env`.

The prod stack already lives in Docker Manager as the `truemain` project
(`/docker/truemain/docker-compose.yml`), so no adoption step is needed — the
action overwrites that project's compose with `compose.prod.yaml` and redeploys.

### Which build is running

The deploy injects `APP_VERSION=<release tag>` alongside `IMAGE_TAG`, and
`compose.prod.yaml` forwards it to the web container as
`NUXT_PUBLIC_APP_VERSION`. The site footer then prints the release it is
serving, small and dimmed (`1.19.0`, with no environment prefix — see
`web/app/utils/app-version.ts`). That is the prod half of the preprod
`<base>-rc.<N>` stamp described in `docs/preprod.md`: together they make "is
this change live, and where?" answerable from the page.

Because it is a runtime variable rather than a build arg, a manual redeploy that
doesn't set `APP_VERSION` simply hides the label instead of showing a stale one.

### Applying migrations before the deploy

The `migrate` job of the rollout runs between `publish` and `deploy` and applies
pending EF migrations as an idempotent SQL script — see
`docs/production-migrations.md` for why this replaced startup migrations. Its
SSH secrets are part of the same `preflight` check: since
`Database__ApplyMigrationsOnStartup` is permanently `false` in
`compose.prod.yaml`, letting the deploy proceed without a migration
attempt would silently roll a new image against a possibly-stale schema.

| Kind     | Name                 | Value                                                        |
| -------- | -------------------- | ------------------------------------------------------------ |
| secret   | `PROD_SSH_HOST`      | the prod VPS address (same host `ssh prod` in `~/.ssh/config` points at) |
| secret   | `PROD_SSH_KEY`       | private key for a dedicated CI-only key authorized as `root` on the VPS, **not** the personal `~/.ssh/id_ed25519` — same `root` account (Docker group membership is root-equivalent anyway), but a separately revocable key |
| variable | `PROD_SSH_HOST_KEY`  | the VPS's SSH host public key, `known_hosts` format (pin this instead of trusting `ssh-keyscan` fresh on every run) |

Postgres only listens on the VPS-internal Docker network (no published port),
so the job connects over SSH and pipes the generated script into `psql`
running inside the already-live `truemain-postgres` container, using the
`POSTGRES_USER`/`POSTGRES_DB` already set in `/docker/truemain/.env` — the
same credential the app itself connects with. The deploy depends on this
job succeeding, so a failed or skipped migration blocks the image roll rather
than shipping a schema mismatch.

`PROD_SSH_KEY`'s public half is installed in the VPS's `~/.ssh/authorized_keys`
with a forced `command=/usr/local/bin/apply-migration.sh` (plus
`no-pty`/`no-port-forwarding`/`no-agent-forwarding`/`no-X11-forwarding`) —
whatever the CI step sends as a remote command is ignored, so a leaked key
can only ever pipe SQL into that one fixed `psql` invocation, never get a
general root shell. The script itself (owning the container name and compose
path) lives on the VPS, not in the workflow.

### Manual fallback

```bash
cd /docker/truemain
docker compose pull
docker compose up -d
```

If `compose.prod.yaml` itself changed in the release, re-download it before
pulling. The file sets `pull_policy: always` on the five application services
(api, web, admin and both ingestor lanes)
for exactly this path: with no `IMAGE_TAG` in the env the images resolve to
the moving `:latest`, which is already present locally and would otherwise be
silently reused (#765). The CI rollout passes an immutable `IMAGE_TAG`, so a
pull is implied there anyway.

## The edge (Caddy)

Caddy is the only public entry point: it listens on 80/443, terminates TLS and
proxies to web, admin and Umami over the internal compose network. Its
configuration is **inline in `compose.prod.yaml`**, under
`configs.edge_caddyfile`, mounted at `/etc/caddy/Caddyfile` — there is no
`Caddyfile` in the repository and none on the host. Edit the compose file to
change the edge; the change reaches prod with the release that contains it.

It used to be a tracked `Caddyfile` bind-mounted from the deployment directory,
but the deploy only ever writes the compose file and the `.env` (`docs/ci.md`,
*What the deploy writes on the VPS*), so the host copy stayed whatever was last
copied by hand. Compression (#1583) and the security headers (#1696) were both
released green and absent from prod until someone copied the file over —
#1598. The `edge` job of `deploy-prod.yml` now checks the live site after every
rollout (`docs/ci.md`, *Verifying the edge*).

- **Certificates.** Caddy obtains and auto-renews a Let's Encrypt certificate
  for every site address. Certificates and the ACME account live in the
  `truemain_caddy_data` volume so they survive restarts; re-issuing on every
  boot would hit Let's Encrypt rate limits. Ports 80 and 443 must be open to
  the internet (HTTP-01 / TLS-ALPN-01), and the A records of `truemain.lol`,
  `www`, `admin` and `analytics` must point at the VPS, DNS-only.
- **Sites.** `truemain.lol` proxies to web, `www.truemain.lol` redirects
  permanently to the apex, `admin.truemain.lol` proxies to admin and
  `analytics.truemain.lol` to Umami.
- **Access logs.** Caddy writes none unless a site asks, so `log` is opted
  into on the public site (bot crawls, 404s, upstream errors) and the admin
  (authentication attempts) only. Output goes to stderr, capped by Docker's
  json-file driver at 3 x 10 MB per container, so it cannot fill the disk and
  `docker logs` on the Caddy container shows it. That budget is shared, which
  is why the analytics site stays silent: the Umami tracker fires on every page
  view and would evict everything else.
- **Admin `X-Forwarded-For`.** The admin site sets `X-Forwarded-For` to the
  real peer, dropping any client-supplied value. Caddy does this by default
  (the browser is an untrusted peer); stating it keeps the invariant the
  admin's per-IP login throttle relies on (it trusts the header only when
  `NUXT_TRUST_PROXY` is set) robust across Caddy versions.
- **Umami framing.** Umami bakes `frame-ancestors 'self'` into its CSP at
  image build time (`ALLOWED_FRAME_URLS` only applies to source builds), which
  would block the admin's Analytics iframe, so the analytics site rewrites that
  directive on the way out to also allow the admin origin.

## Compression at the edge

Caddy compresses what it proxies for the public site and the admin (`encode zstd gzip` in the edge configuration, #1583),
choosing zstd or gzip from the browser's `Accept-Encoding`. Nothing upstream compresses: until then a champion
page's HTML (about 200 KB), each JS bundle (up to about 290 KB) and `/api/static/items` (about 500 KB) went out
raw. Images from `/_ipx` are already WebP and gain little. Preprod's edge Caddy carries the same directive, so its
page-load measurements stay comparable.

## Security response headers

The edge sets the response security headers the app frameworks do not (the public site and admin shipped none). A shared `(security_headers)` snippet in the edge configuration carries `Strict-Transport-Security` (one year, `includeSubDomains`), `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: strict-origin-when-cross-origin`, a `Permissions-Policy` that turns off camera/microphone/geolocation/topics, and strips the upstream `X-Powered-By`. It is imported into the two app-owned vhosts only — never the `analytics` vhost, where Umami ships its own CSP and an `X-Frame-Options` here would fight the admin Analytics iframe that block deliberately allows.

Each app vhost then adds its own `Content-Security-Policy`, because the allowed origins differ: the public site loads the Umami tracker (so its host is in `script-src`/`connect-src`), while the admin embeds the Umami dashboard in an iframe (so its host is in `frame-src` instead). Both keep `'unsafe-inline'` for scripts and styles — Nuxt's hydration and the charts' inline styles need it, and there is no nonce pipeline — and allow the two game-asset CDNs (`ddragon.leagueoflegends.com`, `raw.communitydragon.org`) under `img-src` for the icons that do not arrive same-origin through `/_ipx`. Preprod's inline edge carries the same snippet minus HSTS (it is plain HTTP) and minus the CSP (its analytics host is an operator env value, not a fixed origin); the CSP is prod-only, where the origins are static.

## Postgres server tuning

`compose.prod.yaml` starts Postgres with explicit `-c` settings (part A of #1366).
Host facts measured on 2026-09-02: 4 vCPU, 16 GB RAM, NVMe, a ~38 GB database,
and Postgres is the dominant tenant (8.8 GB RSS, ~13 GB of the host in page
cache). Until then the server ran on the compiled defaults, sized for a 1 GB
machine: 128 MB `shared_buffers`, 4 MB `work_mem`, `random_page_cost=4` on
flash. The sizing is pgtune-style for a "mixed" workload on that host:

| Setting | Value | Why |
| ------- | ----- | --- |
| `shared_buffers` | 4GB | 25% of RAM, the usual ceiling before double buffering hurts |
| `effective_cache_size` | 11GB | planner hint only; matches the page cache actually observed |
| `work_mem` | 32MB | per sort/hash node; the 2.7M-row `match_participants` folds spilled to disk at 4MB |
| `hash_mem_multiplier` | 2 | hash joins and aggregates may use 64MB before spilling |
| `maintenance_work_mem` | 1GB | autovacuum and index builds on the 13GB snapshot table |
| `random_page_cost` / `effective_io_concurrency` | 1.1 / 200 | NVMe, not spinning rust |
| `jit` | off | pure overhead on these short analytic queries |
| `max_parallel_workers_per_gather` | **0** | **do not remove**, see below |
| `wal_compression`, `max_wal_size`, `min_wal_size`, `checkpoint_completion_target` | lz4, 4GB, 1GB, 0.9 | fewer, smoother checkpoints |
| `autovacuum_vacuum_cost_delay` / `_vacuum_scale_factor` / `_analyze_scale_factor` | 2ms / 0.05 / 0.02 | the big tables bloat faster than the stock scale factors react |
| `shared_preload_libraries` | pg_stat_statements | statement-level visibility; the extension itself is created by an EF migration |

**`max_parallel_workers_per_gather=0` is the deliberate fix for the `/dev/shm`
exhaustion incident (#589)**: parallel workers on the aggregate-pattern queries
exhausted the shared memory segment, Postgres raised `53100`, and the API and
ingestor crash-looped. Re-enabling parallelism reproduces that outage. The
container's `shm_size` was still raised from 256m to 1g, because parallel-query
and hash workers allocate their shared segments there and 256m is what got
exhausted; cheap insurance now that the server is allowed to use real memory.

Preprod (`compose.preprod.yaml`) runs the same *settings*, with the memory ones
scaled to its host rather than copied (#1558). Measured on 2026-09-14 it has
2 vCPU and 7.7 GB of RAM, shared with other containers, against prod's 4 and 16.
The rule is prod's ratio, not prod's value: `shared_buffers` at a quarter of RAM
(2GB), `effective_cache_size` at about 70% (5GB), `work_mem` and
`maintenance_work_mem` halved with the RAM (16MB, 512MB), and the WAL sizes halved
with the smaller disk (2GB / 512MB). Every setting that is not a size — jit off,
flash-priced random access, no parallel workers, the autovacuum factors — is
identical, so preprod exercises the plans prod will. Absolute timings measured on
preprod still underestimate prod's capacity: it has half the cores, and shares
them.

## Connection pools

Every service reaches Postgres through PgBouncer in transaction mode, configured by the `pgbouncer_ini` config
inline in each compose file. All services connect as the same user, to two PgBouncer databases that point at the
same Postgres database: the API to `${POSTGRES_DB}_api`, both ingestor lanes to `${POSTGRES_DB}`. Each has its own
pool of 25 server connections (`default_pool_size`) plus 5 in reserve, and `max_user_connections = 30` caps the two
together at what the single shared pool allowed before the split; PgBouncer closes an idle connection of one pool
to open one for the other. The same values run in every compose file, preprod included.

| Setting | Value | Why |
| --- | --- | --- |
| API `Maximum Pool Size` | 30 | what PgBouncer can actually serve (25 + 5 reserve). At 100, a load test queued 74 API clients at PgBouncer, with waits up to 11 s and requests hanging until the visitor gave up (#1570). Excess requests now wait in Npgsql and fail after its 15 s connection timeout, with a logged error |
| Ingestors' `Maximum Pool Size` | 40 each | unchanged; their batches hold a connection across a pass |
| PgBouncer `query_wait_timeout` | 30 s | ends a client's wait for a server connection. The longest wait measured under overload was 11 s, so it only cuts a wait that would otherwise last minutes; it applies to the ingestors too |
| `statement_timeout`, API only | 60 s | set by the `connect_query` of `${POSTGRES_DB}_api`, so Postgres itself cancels a runaway API read. 60 s is the visitor's own timeout in the #1570 load test, past which nobody is waiting for the answer (#1631) |
| API `Command Timeout` | 65 s | just above the server-side limit, so the server's `canceling statement due to statement timeout` is the error that gets logged, not Npgsql's client-side timeout |
| `idle_in_transaction_session_timeout`, every pooled session | 60 s | ends a session left idle inside an open transaction, which would keep its locks and hold back vacuum. Every ingestor transaction wraps writes only: the folds and Riot calls run before `BEGIN`, so the idle gaps inside one are milliseconds |
| Ingestors' `Command Timeout` | 300 s, no `statement_timeout` | the chunked folds legitimately run long (#603, #632, #988) |

These settings are applied per server connection by `connect_query`, not by a plain `SET`: in transaction mode a
session `SET` lands on whichever server connection the transaction borrowed and leaks to the next client. The
startup `options` route does not work either, since PgBouncer only accepts parameters it tracks, and
`statement_timeout` is not one. Nothing in the backend resets them: Npgsql runs with `No Reset On Close=true`, and
PgBouncer only sends its `server_reset_query` in session mode. To check them, connect with `psql` through PgBouncer (port 6432)
to `${POSTGRES_DB}_api` or `${POSTGRES_DB}` and run `SHOW statement_timeout; SHOW idle_in_transaction_session_timeout;`.

## Ingestor tuning knobs

`compose.prod.yaml` overrides the ingestor's app settings for full-volume
ingestion. The knobs are shared by both lanes (`ingestor` on `FetchLane`,
`ingestor-aggregate` on `AggregateLane`, #1490) through one YAML anchor, and
every step below runs on the fetch lane. Two of them deserve a note because
their unit is easy to misread:

- **`ManualSeed__BatchSize` (750).** Manual-seed intake is FIFO and strictly
  batched, so a large backlog drains at a fixed rate rather than being spent
  at once. The unit is **one batch per full pipeline cycle**: `ManualSeed` is a
  step in the fetch lane's sequence, which runs once per container pass. The
  measured cadence is what to size against, not a nominal interval: prod ran
  55 fetch passes on 2026-09-05, and the lane split raises that by taking
  aggregation out of the same queue.
  That unit made the previous value of 200 look four times stronger than it
  was: 200/cycle is ~2.5–4.8k seeds a day, and the per-champion dpm sweep puts
  tens of thousands in the queue in one run, two to three weeks of drain.
  At 55 passes a day, 750 is ~41k seeds a day. A resolved request costs three Riot calls (account-v1,
  summoner-v4, champion mastery, see `ManualSeedProcess.ProcessRequestAsync`);
  one whose Riot ID no longer exists stops after the first, so that is the
  ceiling on paper — the real figure is bounded by the Riot budget the lane
  actually has, and lower again by whatever share of the queue has gone stale. This is a knob for a backlog, not a steady state:
  turn it back down once the queue is drained.
- **`MatchIngestion__BatchSize`** is the next bottleneck: seeding faster gets
  accounts registered faster, their matches still ingest at that many per
  cycle.

`STORAGE_DISK_CAPACITY_BYTES` is the volume size the admin storage forecast
projects against (#925); unset means no forecast, which the panel says rather
than fitting a line to a guessed capacity.
