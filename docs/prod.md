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
- **Full-volume ingestion** — `compose.prod.yaml` keeps the default
  data-diet knobs (see the table in `docs/preprod.md` for the prod defaults).

## Updating prod to the latest release

### Automatic (Hostinger Docker Manager API)

The `deploy-prod` job in `deploy-prod.yml` redeploys the `truemain` Docker
Manager project right after the release images are published, using the
official `hostinger/deploy-on-vps` action (a pure API call — no SSH material in
CI). It is a no-op until three pieces of repository configuration exist:

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

The job is gated on the `HOSTINGER_PROD_VM_ID` variable, and a guard step skips
the deploy (green no-op) while `PROD_ENV_FILE` is empty — the action overwrites
the project `.env` on every run, so deploying with an empty secret would wipe
the prod `.env`. Auto-deploy therefore only becomes active once all three are
set.

The prod stack already lives in Docker Manager as the `truemain` project
(`/docker/truemain/docker-compose.yml`), so no adoption step is needed — the
action overwrites that project's compose with `compose.prod.yaml` and redeploys.

### Manual fallback

```bash
cd /docker/truemain
docker compose pull
docker compose up -d
```

If `compose.prod.yaml` itself changed in the release, re-download it before
pulling.
