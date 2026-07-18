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
  requires starting from an empty database.
- **Tiny database** — `compose.preprod.yaml` overrides the ingestor's
  app settings so every pipeline stage runs (discovery, harvest, scoring,
  match ingestion, main analysis, aggregations, retention) but per-run volumes
  are small and only the current patch's match data is retained. The
  accounts/mains base is never purged by retention — only match data is — so
  the player base persists while matches stay bounded.

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

The compose file uses `truemain_preprod_*` volume names, so even on the old
production host the stack starts from a virgin Postgres/Mongo. The API applies
EF migrations on startup (`Database__ApplyMigrationsOnStartup=true`); the
ingestor then populates the database over its cycles.

Exposed ports (HTTP, no TLS — restrict by firewall to trusted IPs):

| Service  | Port |
| -------- | ---- |
| web      | 3001 |
| admin    | 3002 |
| api      | 8081 |
| pgadmin  | 5051 |
| postgres | 5432 (loopback only) |

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
service in `compose.preprod.yaml` (they override `appsettings.json`; see
`backend/Ingestor/Options/*` for the full catalogue):

| Knob | Preprod | Prod default | Effect |
| ---- | ------- | ------------ | ------ |
| `MatchDataRetention__RetainedPatchCount` | 1 | 2 | keep only the current patch's match data |
| `Discovery__MaxAccountsPerPlatformPerRun` | 100 | 500 | smaller ladder crawl window |
| `Discovery__NewAccountsTarget` | 15 | 50 | fewer new accounts per run |
| `Scoring__TopNPerPlatform` | 50 | 200 | fewer candidates queued per platform |
| `Harvest__MaxCandidatesPerRun` | 500 | 5000 | cap harvest candidate generation |
| `MatchIngestion__BatchSize` | 25 | 50 | fewer accounts fetched per cycle |
| `MatchIngestion__MatchesPerAccount` | 10 | 20 | fewer matches per account |
| `MainAnalysis__MatchesToConsider` | 30 | 50 | smaller analysis window |
| `MainAnalysis__MinMatchesToEvaluate` | 10 | 20 | flag mains sooner on the small sample |

Adjust them directly in the compose file on the host if preprod needs more (or
less) data — no image rebuild required, `docker compose up -d` recreates the
ingestor with the new values.
