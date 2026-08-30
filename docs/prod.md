# Production environment

Production runs the released images (`:latest`, tagged with the release
version), which are built and published when a GitHub Release is published
(see `.github/workflows/deploy-prod.yml`). The stack is defined by
`compose.prod.yaml`.

Design goals:

- **Tracks releases** — every published release rebuilds the images and, once
  the Hostinger credentials are configured, redeploys the VPS automatically.
- **Production Riot API key** — never the preprod key. PUUIDs are encrypted
  per API app, so the key and the database form an inseparable pair.
- **Full-volume ingestion** — `compose.prod.yaml` runs the largest data-diet
  knobs (see the table in `docs/preprod.md`); most are explicit overrides now
  (#811), a few still fall back to the `appsettings.json` defaults.

## Updating prod to the latest release

### Automatic (Hostinger Docker Manager API)

The `deploy-prod` job in `deploy-prod.yml` redeploys the `truemain` Docker
Manager project right after the release images are published, using the
official `hostinger/deploy-on-vps` action (a pure API call — no SSH material in
CI). It needs three pieces of repository configuration:

| Kind     | Name                    | Value                                                        |
| -------- | ----------------------- | ------------------------------------------------------------ |
| variable | `HOSTINGER_PROD_VM_ID`  | the prod VPS id from the Hostinger API                       |
| secret   | `HOSTINGER_PROD_API_KEY`| API token from the **prod** Hostinger account (hPanel → Account → API) |
| secret   | `PROD_ENV_FILE`         | newline-separated `KEY=value` pairs mirroring the VPS `.env` |

Prod and preprod are on **separate Hostinger accounts**, so an API token is
account-scoped: prod uses its own `HOSTINGER_PROD_API_KEY`, distinct from
preprod's `HOSTINGER_API_KEY`.

The action points Docker Manager at `compose.prod.yaml` at the released commit,
so the project on the VPS always matches the release. Keep `PROD_ENV_FILE` in
sync when a new variable is added to the compose file.

These are not checked by `deploy-prod` itself but by a `preflight` job that
every other job in the workflow depends on, and which **fails the run** — no
green skip — when any of them (plus the two SSH secrets below) is missing. The
distinction matters because `migrate-prod` runs *before* `deploy-prod`:
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

The `migrate-prod` job runs between `publish` and `deploy-prod` and applies
pending EF migrations as an idempotent SQL script — see
`docs/production-migrations.md` for why this replaced startup migrations. Its
SSH secrets are part of the same `preflight` check: since
`Database__ApplyMigrationsOnStartup` is permanently `false` in
`compose.prod.yaml`, letting `deploy-prod` proceed without a migration
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
same credential the app itself connects with. `deploy-prod` depends on this
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
pulling.
