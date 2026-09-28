# Preprod environment

Preprod is the pre-production stack: it runs the `:preprod` images, which are
built and published from `develop` on every push (see
`.github/workflows/deploy-preprod.yml` and `docs/ci.md`). It replaces the former "QA"
stack and typically lives on a dedicated host (historically the old production
VPS).

Design goals:

- **Tracks `develop`** — updating preprod is just pulling the latest images.
- **Own Riot API key** — never the production key. PUUIDs are encrypted per
  API app, so the key and the database form an inseparable pair: a new key
  requires starting from an empty database — or, when the accounts must be
  kept, the re-resolution procedure in
  [`docs/riot-key-switch.md`](riot-key-switch.md), which is rehearsed here
  before it is run on prod.
- **Tiny database** — `compose.preprod.yaml` overrides the ingestor's
  app settings so every pipeline stage runs (discovery, harvest, scoring,
  match ingestion, main analysis, aggregations, retention) but per-run volumes
  are small and only the current patch's match data is retained. The
  accounts/mains base is never purged by retention — only match data is — so
  the player base persists while matches stay bounded.
- **Prod's parameters, not prod's volume** — everything that shapes behaviour
  matches `compose.prod.yaml`; what may differ, and why, is listed in
  [Parity with prod](#parity-with-prod).

## Two ingestor lanes

Preprod runs the ingestion pipeline as **two containers** (#1362), as prod does since #1491:

| container | `Job:Mode` | cadence |
| --- | --- | --- |
| `truemain-preprod-ingestor` | `FetchLane` | back-to-back (`RunOnce`, restarted by Docker) |
| `truemain-preprod-ingestor-aggregate` | `AggregateLane` | every `INGESTOR_AGGREGATE_INTERVAL_MINUTES` (default 20) |

The two halves have opposite bottlenecks — the fetch lane waits on Riot, the aggregate lane on Postgres — so
chaining them left the API key idle through every aggregation. Splitting them is a deployment choice, not a
code one: a single container on `Job:Mode=Full` still runs all 20 steps in order.

Both lanes share one environment block in `compose.preprod.yaml` (the `x-ingestor-environment` anchor); only
the mode, the cadence, the `Application Name` on the connection string and the crash volume differ. To collapse
preprod back to one lane, set `INGESTOR_JOB_MODE=Full` and stop the aggregate container.

The fetch lane stays on the default `RunOnce` loop: its passes are back-to-back, paced by the per-routing-value
rate limiter rather than by a timer. The aggregate lane runs on a timer (`INGESTOR_AGGREGATE_INTERVAL_MINUTES`,
default 20): there is nothing to gain from re-folding the same rows the moment a pass ends, and a fixed cadence
is what makes "how stale can an aggregate be?" answerable. Each lane has its own crash volume, because two
containers writing crash dumps to one path would race on the file the crash reporter writes before it reaches
Mongo. `MainAnalysis__AggregateNonMainPopulation` is on here (#1346, gated per environment by #1349) because
the widened fold multiplies the aggregation's source rows and that process once OOM-killed a VPS (#601);
measured on 2026-09-01 it ran at ~250 MB RSS with no restart and produced 16.6k non-main scopes on the live
patch alongside 20.9k main ones. Postgres runs the prod tuning at roughly half the values, see `docs/prod.md`.

What to watch while the split is on trial:

- `process_runs` — each lane's steps should keep completing; no run should turn `Abandoned` when the *other*
  lane restarts (that was the bug the scoped reconciliation fixes).
- `pg_stat_activity` — retention (aggregate lane) deletes while the fetch lane inserts. They touch disjoint
  patches by construction, so this should show no lock waits growing over time.
- The Riot usage panel — the point of the split is that the key stops idling during aggregation.


## First deployment on a host (fresh database)

If the host previously ran another TrueMain stack (e.g. the old production),
tear it down first — preprod must not inherit its data:

```bash
# From the directory holding the OLD stack's compose file:
docker compose down --remove-orphans

# Remove the old data volumes (irreversible — make sure any backup you want
# to keep has been taken; the old prod data is unusable with the new Riot key
# anyway because PUUIDs are app-scoped):
docker volume ls | grep truemain
docker volume rm <old truemain volumes…>

# Optional: reclaim disk from now-unused images.
docker image prune -a
```

Then deploy preprod:

```bash
# /docker is where Hostinger's Docker Manager keeps its compose projects —
# deploying there keeps the stack visible/manageable from hPanel.
mkdir -p /docker/truemain-preprod && cd /docker/truemain-preprod
# Fetch the compose file and env template from the repo (develop branch):
# Named docker-compose.yml on the host so plain `docker compose` (and Docker
# Manager) picks it up without -f.
curl -fsSL https://raw.githubusercontent.com/ilyanfraimbault/TrueMain/develop/compose.preprod.yaml -o docker-compose.yml
curl -fsSL https://raw.githubusercontent.com/ilyanfraimbault/TrueMain/develop/.env.preprod.example -o .env

# Fill in the secrets: the NEW preprod Riot API key (RGAPI-…), strong
# Postgres/Mongo/admin passwords, a 32+ char OPS_API_KEY and
# ADMIN_SESSION_PASSWORD (e.g. `openssl rand -hex 32`).
vim .env

docker compose up -d
```

The API container will not create the schema itself (migrations are applied
out-of-band, not on startup — see below): run the `Deploy Preprod` workflow
once via `gh workflow run deploy-preprod.yml` (or the Actions UI) right after
this first bring-up, so the rollout's `migrate` job creates the schema before the API
is expected to serve traffic.

The compose file uses `truemain_preprod_*` volume names, so even on the old
production host the stack starts from a virgin Postgres/Mongo. Migrations are
applied by the rollout's `migrate` job (see below), not at API startup; the
ingestor then populates the database over its cycles.

Exposed ports (HTTP, no TLS — restrict by firewall to trusted IPs):

| Service                 | Port |
| ----------------------- | ---- |
| web (through `caddy`)   | 3001 |
| admin (through `caddy`) | 3002 |
| api                     | 8081 (loopback only) |
| umami-proxy             | 3100 |
| postgres                | 5432 (loopback only) |

`umami-proxy` is the one entry in that table that cannot be narrowed to
loopback: it is what the visitor's browser posts analytics events to
(`UMAMI_PUBLIC_URL`, injected into the frontends as `NUXT_PUBLIC_UMAMI_HOST`),
so binding it to `127.0.0.1` would silently drop every hit. Firewall it to the
tester IPs like the rest, not to the host itself.

## Edge proxy

Web and admin are not published directly: a `caddy` service listens on 3001 and
3002 and proxies to them, the way Caddy fronts prod (#1558). Before it, preprod
differed from prod in three behaviours that all came from having nothing in
front of the apps:

- **Rate limiting.** The API keys its per-visitor limit on the last
  `X-Forwarded-For` hop (#1546). With no edge to write that header, every
  preprod visitor landed in one bucket, so a load test measured the limiter
  rather than the site.
- **Compression.** Both sites compress responses (`encode zstd gzip`), like prod's edge (#1583): HTML,
  bundles and API JSON otherwise travel raw, and a page-load measurement over an uncompressed edge would
  overstate transfer times.
- **Access logs.** Both sites log every request to the Caddy container's output,
  as prod's two sites do.
- **Admin login throttle.** The admin runs with `NUXT_TRUST_PROXY=true`, like
  prod, because Caddy overwrites `X-Forwarded-For` with the peer it saw.

Two things stay different by nature. The Caddyfile is inline in
`compose.preprod.yaml` (`configs.edge_caddyfile`) rather than a file next to it,
because the Docker Manager deploy ships the compose file and nothing else — the
same reason `umami-proxy` is configured that way. And it serves plain HTTP
(`auto_https off`): preprod has no DNS name to get a certificate for, and the
host's 80/443 belong to another project, so the admin session cookie stays
non-`Secure`.

`PREPROD_SITE_URL` and `PREPROD_ADMIN_URL` (in `.env`, so the address stays out
of the repo) are the two origins Caddy serves. The web app advertises the first
in its canonical links, sitemap and robots.txt instead of the prod host, and the
API allows both as CORS origins, as prod lists its own. The deploy fails if
either is unset.

## Updating preprod to the latest develop

### Automatic (Hostinger Docker Manager API)

The `deploy` job of the `rollout` workflow called by `deploy-preprod.yml`
redeploys the `truemain-preprod` Docker Manager project right after the
`:preprod` images are published and the migrations applied, using the official
`hostinger/deploy-on-vps` action (a pure API call — no SSH material in CI). A
`preflight` job fails the whole run, never a green skip, until these three
pieces of repository configuration exist (plus the two SSH secrets below):

| Kind | Name | Value |
| ---- | ---- | ----- |
| variable | `HOSTINGER_PREPROD_VM_ID` | the preprod VPS id (or name) from the Hostinger API |
| secret | `HOSTINGER_PREPROD_API_KEY` | API token generated in hPanel → Account → API, **on the preprod account** |
| secret | `PREPROD_ENV_FILE` | newline-separated `KEY=value` pairs mirroring the VPS `.env` |

The action points Docker Manager at `compose.preprod.yaml` at the deployed
commit, so the project on the VPS always matches the repo. Keep
`PREPROD_ENV_FILE` in sync when a new variable is added to the compose file.

Each build publishes two tags per image: the moving `:preprod` pointer and an
immutable `:<commit-sha>`. The deploy step injects `IMAGE_TAG=<commit-sha>`
into the project env, and the compose file references
`ghcr.io/…/truemain-<svc>:${IMAGE_TAG:-preprod}`. Because the resolved image
name changes on every merge, Docker Manager sees an image it doesn't have
locally and pulls + recreates the containers — a bare `:preprod` would leave
the previous image running, since redeploying an unchanged mutable tag never
recreates anything (#765). `IMAGE_TAG` is unset outside CI, so the manual
fallback below (and a first bring-up) resolves to `:preprod` as before.

### Which build is running (`<base>-rc.<N>`)

Every preprod deploy carries a version, printed small in the site footer
(`preprod · 1.20.0-rc.4`) so "is my change on preprod yet?" and "did this reach
prod?" are answerable from the page instead of by comparing SHAs on GitHub.

The `version` job resolves it before anything is built, by running
`.github/scripts/resolve-preprod-version.sh` (kept out of the workflow so CI can
test the real thing — `resolve-preprod-version.test.sh`, run by the
`Deploy Scripts` job):

- **base** — the next *minor* after the latest **release** tag (`1.19.0` →
  `1.20.0`), i.e. what a plain "release" cuts. It is a working label, not a
  promise: the real bump is still decided by your word at release time (see the
  `release` skill), so a preprod line labelled `1.20.0-rc.*` can perfectly well
  ship as `1.19.1`. When you already know the next one is a major, set the
  `PREPROD_VERSION_BASE` repository variable to the exact `MAJOR.MINOR.PATCH`
  and clear it once that release is cut.
- **N** — the highest existing `<base>-rc.*` counter, +1. It resets on its own
  when a release moves the base, and a deleted tag can never make it reuse a
  number.

`tag` pushes the git tag **after** the VPS has taken the deploy, so a
`-rc.N` tag always means "this ran on preprod" rather than "this was built".
The same string also tags the four images on GHCR (`…/truemain-web:1.20.0-rc.4`),
which is why the version is a semver *prerelease* and not build metadata — a `+`
is illegal in a Docker reference, a `-` is not.

Two consequences worth knowing:

- The workflow takes a **workflow-level** concurrency group. The counter is read
  from the tags on the remote, so two runs resolving a version at once would
  pick the same number. Serialising the pipeline is what keeps the sequence
  gapless; a burst of merges collapses to the newest pending run, and only the
  commit that actually deployed gets a tag.
- Anything reading "the latest version" must filter to bare
  `MAJOR.MINOR.PATCH`. Git's version sort ranks `1.20.0-rc.4` **above**
  `1.20.0`, so an unfiltered `git tag --sort=-v:refname | head -1` would read a
  preprod build as the last release.

### Applying migrations before the deploy

The `migrate` job of the rollout runs between `publish` and `deploy` and
applies pending EF migrations as an idempotent SQL script — see
`docs/production-migrations.md` for why this replaced startup migrations. Its
SSH secrets are part of the same `preflight` check: since
`Database__ApplyMigrationsOnStartup` is permanently `false` in
`compose.preprod.yaml`, letting the deploy proceed without a migration attempt
would silently roll a new image against a possibly-stale schema.

| Kind     | Name                   | Value                                                        |
| -------- | ---------------------- | ------------------------------------------------------------ |
| secret   | `PREPROD_SSH_HOST`     | the preprod VPS address (same host `ssh preprod` in `~/.ssh/config` points at) |
| secret   | `PREPROD_SSH_KEY`      | private key for a dedicated CI-only key authorized as `root` on the VPS, **not** the personal `~/.ssh/id_ed25519` — same `root` account (Docker group membership is root-equivalent anyway), but a separately revocable key |
| variable | `PREPROD_SSH_HOST_KEY` | the VPS's SSH host public key, `known_hosts` format (pin this instead of trusting `ssh-keyscan` fresh on every run) |

Postgres is only bound to `127.0.0.1:5432` on the VPS, so the job connects
over SSH and pipes the generated script into `psql` running inside the
already-live `truemain-preprod-postgres` container, using the
`POSTGRES_USER`/`POSTGRES_DB` already set in `/docker/truemain-preprod/.env`.
The deploy depends on this job succeeding, so a failed or skipped migration
blocks the image roll.

`PREPROD_SSH_KEY`'s public half is installed in the VPS's
`~/.ssh/authorized_keys` with a forced `command=/usr/local/bin/apply-migration.sh`
(plus `no-pty`/`no-port-forwarding`/`no-agent-forwarding`/`no-X11-forwarding`) —
whatever the CI step sends as a remote command is ignored, so a leaked key
can only ever pipe SQL into that one fixed `psql` invocation, never get a
general root shell. The script itself (owning the container name and compose
path) lives on the VPS, not in the workflow.

### Manual fallback

```bash
cd /docker/truemain-preprod
docker compose pull
docker compose up -d
```

If the compose file itself changed on `develop`, re-download it before
pulling.

## Parity with prod

Preprod runs **prod's parameters at a test volume**: it checks that a change
behaves on real data before a release, on a host it shares with other projects.
It is not sized for capacity measurements any more (see *Volume*). Everything in `compose.preprod.yaml` matches
`compose.prod.yaml` except what falls into one of the three groups below; a
difference that fits none of them is drift to fix, not a preprod habit.

### Volume

The ingestion volume is tuned with environment variables shared by both ingestor
lanes (the `x-ingestor-environment` anchor). They override the defaults declared
in `backend/Ingestor/Options/*`:

| Knob | Preprod | Prod | Effect |
| ---- | ------- | ---- | ------ |
| `Discovery__MaxAccountsPerPlatformPerRun` | 100 | 750 | ladder crawl window |
| `Discovery__NewAccountsTarget` | 15 | 75 | new accounts per run |
| `Scoring__TopNPerPlatform` | 50 | 300 | candidates queued per platform |
| `Harvest__MaxCandidatesPerRun` | 500 | 7500 | harvest candidate generation cap |
| `MatchIngestion__BatchSize` | 25 | 75 | accounts fetched per cycle |
| `ManualSeed__BatchSize` | 250 | 750 | manual seeds resolved per cycle |
| `MatchDataRetention__RetainedPatchCount` | 1 | 2 (code default) | patches of match data kept |
| `MatchDataRetention__AggregateRetainedPatchCount` | 2 | 0 (code default: kept forever) | patches of aggregates kept, so older champion pages are empty on preprod by choice |
| `MongoLogging__LogsRetention` | 30 days | 90 days (default) | diagnostic log TTL |
| `MatchIngestion__MatchesPerAccount` | 10 | 20 (default) | matches fetched per account |
| `MainAnalysis__MatchesToConsider` | 30 | 50 (default) | recent matches main analysis reads |
| `MainAnalysis__MinMatchesToEvaluate` | 10 | 20 (default) | matches needed before an account is evaluated |
| web `NITRO_CLUSTER_WORKERS` | 1 | 3 | Node processes serving the public site |

Postgres memory settings follow the same logic: they keep **prod's ratio to the
host's RAM**, not prod's absolute values — see *Postgres server tuning* in
`docs/prod.md`.

The host is CPU-limited by the provider when the stack is busy, so every health
check runs with a 20s timeout and a 90s start period (`docs/ci.md`): a probe that
times out under that limitation would otherwise make a deploy abort on a healthy
container.

Main detection is back at 10/30/10 (2026-09-16). Running prod's 20/50/20, two web
workers and a day of 200-visitor load tests on the shared 2-vCPU host starved the
whole VPS — the preprod stack had to be stopped by hand. A main on preprod is
therefore detected from a smaller sample than on prod; check a main-detection
change against prod's numbers, not preprod's.

Adjust a volume knob directly in the compose file on the host if preprod needs
more (or less) data for a while — no image rebuild required, `docker compose up
-d` recreates the ingestor with the new values. The next deploy puts the
repository's values back.

### Identity

What cannot be the same on two environments: the Riot API key and the database
it is paired with, container, volume and network names, host ports, image tags,
secrets, the public origins (`PREPROD_SITE_URL`, `PREPROD_ADMIN_URL`, Umami's
URLs), `NUXT_PUBLIC_APP_ENV`, and TLS (see [Edge proxy](#edge-proxy)). Preprod
also publishes a debugging aid prod does not have: Postgres on loopback.

### Trials

Behaviour that runs on preprod first, on purpose, and stays listed here until it
is promoted to prod or dropped:

- `MainAnalysis__AggregateNonMainPopulation=true` — the "everyone" population
  fold (#1346). Prod keeps it off until its memory cost is validated there
  (#601).

## Platform scope

The regions the pipeline runs on live in **one** list, `Platforms:Active`
(`Platforms__Active__0`, `Platforms__Active__1`, … as environment variables).
Discovery, match ingestion and the harvest inherit it, so a region is added in
a single place instead of three.

A section can still narrow the scope with its own `Discovery__Platforms__0`,
`MatchIngestion__Platforms__0` or `Harvest__Platforms__0`, but the override is
validated at startup: it must be a subset of `Platforms:Active`, and
`Harvest:Platforms` must additionally be a subset of
`MatchIngestion:Platforms` (the harvest only sees matches we ingest). A
divergent configuration fails the ingestor boot with an explicit message
instead of silently skipping the region for one stage (#496).
