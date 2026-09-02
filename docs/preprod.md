# Preprod environment

Preprod is the pre-production stack: it runs the `:preprod` images, which are
built and published from `develop` on every push (see
`.github/workflows/deploy-preprod.yml`). It replaces the former "QA"
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

## Two ingestor lanes

Preprod runs the ingestion pipeline as **two containers** (#1362), where prod still runs one:

| container | `Job:Mode` | cadence |
| --- | --- | --- |
| `truemain-preprod-ingestor` | `FetchLane` | back-to-back (`RunOnce`, restarted by Docker) |
| `truemain-preprod-ingestor-aggregate` | `AggregateLane` | every `INGESTOR_AGGREGATE_INTERVAL_MINUTES` (default 20) |

The two halves have opposite bottlenecks — the fetch lane waits on Riot, the aggregate lane on Postgres — so
chaining them left the API key idle through every aggregation. Splitting them is a deployment choice, not a
code one: a single container on `Job:Mode=Full` still runs all 20 steps in order, which is what prod does.

Both lanes share one environment block in `compose.preprod.yaml` (the `x-ingestor-environment` anchor); only
the mode, the cadence, the `Application Name` on the connection string and the crash volume differ. To collapse
preprod back to one lane, set `INGESTOR_JOB_MODE=Full` and stop the aggregate container.

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
this first bring-up, so `migrate-preprod` creates the schema before the API
is expected to serve traffic.

The compose file uses `truemain_preprod_*` volume names, so even on the old
production host the stack starts from a virgin Postgres/Mongo. Migrations are
applied by the `migrate-preprod` CI job (see below), not at API startup; the
ingestor then populates the database over its cycles.

Exposed ports (HTTP, no TLS — restrict by firewall to trusted IPs):

| Service     | Port |
| ----------- | ---- |
| web         | 3001 |
| admin       | 3002 |
| api         | 8081 |
| umami-proxy | 3100 |
| pgadmin     | 5051 |
| postgres    | 5432 (loopback only) |

`umami-proxy` is the one entry in that table that cannot be narrowed to
loopback: it is what the visitor's browser posts analytics events to
(`UMAMI_PUBLIC_URL`, injected into the frontends as `NUXT_PUBLIC_UMAMI_HOST`),
so binding it to `127.0.0.1` would silently drop every hit. Firewall it to the
tester IPs like the rest, not to the host itself.

## Updating preprod to the latest develop

### Automatic (Hostinger Docker Manager API)

The `deploy-preprod` job in `deploy-preprod.yml` redeploys the
`truemain-preprod` Docker Manager project right after the `:preprod` images are
published, using the official `hostinger/deploy-on-vps` action (a pure API
call — no SSH material in CI). It is a no-op until three pieces of repository
configuration exist:

| Kind | Name | Value |
| ---- | ---- | ----- |
| variable | `HOSTINGER_PREPROD_VM_ID` | the preprod VPS id (or name) from the Hostinger API |
| secret | `HOSTINGER_API_KEY` | API token generated in hPanel → Account → API |
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

`tag-preprod` pushes the git tag **after** the VPS has taken the deploy, so a
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

The `migrate-preprod` job runs between `publish-preprod` and `deploy-preprod`
and applies pending EF migrations as an idempotent SQL script — see
`docs/production-migrations.md` for why this replaced startup migrations.
Unlike the `deploy-preprod` guard on `PREPROD_ENV_FILE`, this one fails the
job (not a green skip) when its secrets are missing: since
`Database__ApplyMigrationsOnStartup` is permanently `false` in
`compose.preprod.yaml`, letting `deploy-preprod` proceed without a migration
attempt would silently roll a new image against a possibly-stale schema.

| Kind     | Name                   | Value                                                        |
| -------- | ---------------------- | ------------------------------------------------------------ |
| secret   | `PREPROD_SSH_HOST`     | the preprod VPS address (same host `ssh preprod` in `~/.ssh/config` points at) |
| secret   | `PREPROD_SSH_KEY`      | private key for a dedicated CI-only key authorized as `root` on the VPS, **not** the personal `~/.ssh/id_ed25519` — same `root` account (Docker group membership is root-equivalent anyway), but a separately revocable key |
| variable | `PREPROD_SSH_HOST_KEY` | the VPS's SSH host public key, `known_hosts` format (pin this instead of trusting `ssh-keyscan` fresh on every run) |

Postgres is only bound to `127.0.0.1:5432` on the VPS, so the job connects
over SSH and pipes the generated script into `psql` running inside the
already-live `truemain-preprod-postgres` container, using the
`POSTGRES_USER`/`POSTGRES_DB` already set in `/docker/truemain-preprod/.env`.
`deploy-preprod` depends on this job succeeding, so a failed or skipped
migration blocks the image roll.

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

## Data-diet knobs

The ingestion volume is tuned with environment variables on the `ingestor`
service in `compose.preprod.yaml` and `compose.prod.yaml` (they override
`appsettings.json`; see `backend/Ingestor/Options/*` for the full catalogue).
Prod's match-search knobs are explicit `compose.prod.yaml` overrides (#811),
not `appsettings.json` defaults — they were raised once the champion
matchup/lead aggregation stopped dominating the loop's cycle time:

| Knob | Preprod | Prod | Effect |
| ---- | ------- | ---- | ------ |
| `MatchDataRetention__RetainedPatchCount` | 1 | 2 (appsettings default) | keep only the current patch's match data |
| `Discovery__MaxAccountsPerPlatformPerRun` | 100 | 750 | ladder crawl window |
| `Discovery__NewAccountsTarget` | 15 | 75 | new accounts per run |
| `Scoring__TopNPerPlatform` | 50 | 300 | candidates queued per platform |
| `Harvest__MaxCandidatesPerRun` | 500 | 7500 | harvest candidate generation cap |
| `MatchIngestion__BatchSize` | 25 | 75 | accounts fetched per cycle |
| `MatchIngestion__MatchesPerAccount` | 10 | 20 (appsettings default) | matches per account |
| `MainAnalysis__MatchesToConsider` | 30 | 50 (appsettings default) | analysis window |
| `MainAnalysis__MinMatchesToEvaluate` | 10 | 20 (appsettings default) | flag mains sooner on the small sample |

Adjust them directly in the compose file on the host if preprod needs more (or
less) data — no image rebuild required, `docker compose up -d` recreates the
ingestor with the new values.

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
