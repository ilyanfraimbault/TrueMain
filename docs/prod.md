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

Every service reaches Postgres through PgBouncer in transaction mode: 25 server connections (`DEFAULT_POOL_SIZE`)
plus 5 in reserve, shared by the API and both ingestor lanes, which all connect as the same user. The same values
run in every compose file, preprod included.

| Setting | Value | Why |
| --- | --- | --- |
| API `Maximum Pool Size` | 30 | what PgBouncer can actually serve (25 + 5 reserve). At 100, a load test queued 74 API clients at PgBouncer, with waits up to 11 s and requests hanging until the visitor gave up (#1570). Excess requests now wait in Npgsql and fail after its 15 s connection timeout, with a logged error |
| Ingestors' `Maximum Pool Size` | 40 each | unchanged; their batches hold a connection across a pass |
| PgBouncer `QUERY_WAIT_TIMEOUT` | 30 s | ends a client's wait for a server connection. The longest wait measured under overload was 11 s, so it only cuts a wait that would otherwise last minutes; it applies to the ingestors too |

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
