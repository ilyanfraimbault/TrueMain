# CI/CD

The configuration files under `.github/`, the Dockerfiles and the compose
stacks carry no comments on purpose. This page is where their rationale lives;
`docs/preprod.md`, `docs/prod.md` and `docs/production-migrations.md` cover the
two environments and the migration path in detail.

## Workflows

| Workflow | Trigger | What it does |
| -------- | ------- | ------------ |
| `ci.yml` | PRs, pushes to `develop`/`master`, manual | Build, test and sanity-check whatever the change touches |
| `claude-review.yml` | PRs to `develop`/`master` | Automated formal code review |
| `deploy-preprod.yml` | push to `develop`, manual | Preflight → version → publish images → roll out → tag |
| `deploy-prod.yml` | GitHub Release published | Preflight → publish images → roll out |
| `build-images.yml` | called by both deploys | Builds and pushes the four images with the requested tags |
| `rollout.yml` | called by both deploys | Applies migrations over SSH, then redeploys the Docker Manager project |

`.github/actions/migration-script` is the composite action every job that
needs the idempotent EF migration script goes through (`migrate-fresh` in CI,
`migrate` in the rollout), so the script that is validated is the script that
is deployed: same .NET SDK, same `dotnet-ef` version, same command.

`.github/scripts/resolve-preprod-version.sh` computes the `<base>-rc.<N>`
preprod version. It lives in its own file, with its own test
(`resolve-preprod-version.test.sh`), so the logic under test is the logic that
ships.

## CI: only the jobs the change can break

Every PR run starts with a `changes` job (`dorny/paths-filter`) that decides
which downstream jobs are worth a runner:

| Output | Paths | Gates |
| ------ | ----- | ----- |
| `backend` | `backend/**` | backend build + unit + integration tests, API and Ingestor image builds |
| `data` | `backend/Data/**` | `migrate-fresh` |
| `web` / `admin` | `web/**` / `admin/**` | the frontend job for that app, its image build |
| `compose` | `compose*.yaml`, `.env*.example` | compose config validation |
| `scripts` | `.github/scripts/**` | deploy-script tests |
| `ci` | `ci.yml`, `.github/actions/**` | everything |

Pushes to `develop`/`master` and manual runs are always exhaustive: the push to
`develop` is the commit the preprod deploy builds, and the `develop→master`
merge commit is the one a release is cut from. A squash merge also produces a
tree no PR run ever tested, so the integration suite runs there too.

Docs-only PRs (`**.md`, `docs/**`, `.claude/**`, `LICENSE`) trigger no CI run at
all; the Claude review still runs. Draft PRs are skipped until marked ready,
which re-triggers the workflow. Branch protection requires no status check, so
a skipped job never blocks a merge.

The frontend and Docker jobs take their matrix from `changes` as JSON
(`frontend_apps`, `images`), which is why they read `fromJSON(...)` instead of
a static list. The compose validation is a single job looping over the four
files rather than a matrix: each validation takes seconds and a runner costs
more than the work. Every job has a `timeout-minutes` so a hung Testcontainers
run cannot sit for six hours.

### Backend

CI builds **Release** with code-style analyzers as errors, so a Debug build
passing locally proves nothing. Unit and integration tests run on the same
runner after one build: splitting them would cost a second restore and build
to parallelise a suite that runs in about the same time.

### Frontend

The npm bundled with Node 24 drifts with every patch release, and older npm
resolves sharp's platform-specific optional dependencies differently: a lock
file regenerated with the wrong one installs fine locally and breaks the web
build in CI. The `Pin npm` step installs `npm@11.13.0`, the version both lock
files are generated with (`npx npm@11.13.0 install` locally, see `CLAUDE.md`).

`nuxt typecheck` can pass on stale `.nuxt` types while `nuxt build` fails, so
the job runs typecheck, the vitest suite and a fresh build.

### Migrations on a fresh database

Preprod and prod already carry every migration in `__EFMigrationsHistory`, so
the idempotent guards skip every block there. A statement Postgres refuses
inside a transaction (`CREATE INDEX CONCURRENTLY`, `VACUUM`…) is therefore
invisible until a database is built from scratch: a new preprod, a disaster
recovery restore, onboarding (#1227). `migrate-fresh` applies the generated
script with `psql --single-transaction` to a blank `postgres:17.2` service
container (the version both VPS run), applies it a second time to prove it is
a no-op, and asserts `__EFMigrationsHistory` holds one row per migration on
disk.

### Docker build sanity

The image builds in CI never push. They exist because the images build with
the base image's own npm and SDK rather than the pinned CI toolchain, so a
lock file that installs in the frontend job can still fail inside the image
(#1236). Layer caches are scoped per image (`type=gha,scope=<image>`) and
shared with the deploy builds.

## Deploys

### Immutable tags

Both deploys publish an immutable tag alongside the moving one: `:<sha>` and
`:<base>-rc.<N>` next to `:preprod`, `:<version>` next to `:latest`. The
compose files reference `${IMAGE_TAG:-preprod}` / `${IMAGE_TAG:-latest}`, and
the rollout injects `IMAGE_TAG` with the immutable value. Handing Docker
Manager an unchanged compose spec never triggers a pull or a recreate, so a
mutable-tag redeploy went green while the VPS kept running days-old containers
(#765). `compose.prod.yaml` also sets `pull_policy: always` for the manual
fallback in `docs/prod.md`, which has no `IMAGE_TAG` and resolves to the
already-present `:latest`.

`APP_VERSION` rides the same channel into `NUXT_PUBLIC_APP_VERSION`, so the
footer states which build serves the page (`preprod · 1.20.0-rc.4`, `1.19.0`
in prod). It is a deploy-time value, not a build arg: the image stays
promotable as-is and a version that changes every merge never invalidates a
layer. A manual redeploy without it simply hides the label.

Prod builds and checks out the release tag rather than the triggering ref: a
release can be published from a tag that is no longer the tip of any branch.

### Preflight

Both pipelines start with a `preflight` job that fails the run when any piece
of the deployment configuration is missing (SSH host and key, env file,
Hostinger API key, VM id). Never a green skip: migrations apply before the
images roll, so a deploy-side configuration checked at deploy time could only
ever skip the image roll after the schema had already moved, leaving the old
binary against the new schema (#1259). The env file must be non-empty in
particular because `hostinger/deploy-on-vps` overwrites the project `.env` on
every run.

### Rollout order and concurrency

`rollout.yml` runs `migrate` then `deploy`; the deploy depends on the
migration succeeding, so a failed script blocks the image roll
(`docs/production-migrations.md`). The migration script is printed to the log
and uploaded as an artifact with 90-day retention, the only place the SQL is
visible before it applies.

Each deploy workflow takes a **workflow-level** concurrency group with
`cancel-in-progress: false`. On preprod the rc counter is derived from the
tags already on the remote, so two runs resolving a version at once would land
on the same number; on prod two releases back to back would race for the
moving `:latest`. A running deploy is never cancelled, GitHub collapses the
pending queue to the newest run, and on preprod only a commit that actually
deployed gets its `-rc.N` tag (`tag` runs last).

### Preprod versioning

`version` resolves `<base>-rc.<N>` from a full-history checkout: the base is
the next minor after the latest bare `MAJOR.MINOR.PATCH` tag (or the
`PREPROD_VERSION_BASE` repository variable when the next release is known to
be a major), N is the highest existing counter on that base plus one. Git's
version sort ranks `1.20.0-rc.4` above `1.20.0`, so anything reading "the
latest release" must filter to bare `MAJOR.MINOR.PATCH`. `-rc.` and `.` are
legal in a Docker reference, `+` is not, which is why the version is a semver
prerelease and not build metadata.

## Claude review

`claude-review.yml` posts inline comments prefixed `BLOCKING:` or `NIT:` and
always ends with one formal review: approve when nothing is blocking, request
changes otherwise. The prompt defines blocking narrowly (bugs, security, data
loss, regressions, missing tests, project rules CI cannot catch) and forbids
flagging versions or APIs from memory, because the repository regularly runs
tooling newer than the model's training data. Dependabot is in `allowed_bots`
so its PRs get a verdict too; the prompt tells the reviewer what to check on a
dependency bump. Editing the workflow file makes its own check fail on that
PR; it passes again post-merge. Verdicts trail one commit behind pushes: check
the review's `commit_id` against the head SHA before acting on it.

## Dependabot

One weekly PR per ecosystem groups every minor and patch update; majors stay
individual so a breaking change is reviewed on its own. `docker-compose`
watches the third-party images pinned in the stacks (postgres, pgbouncer,
mongo, umami, caddy, pgadmin), which no Dockerfile references; our own
`ghcr.io/ilyanfraimbault/truemain-*` images are ignored there because their
tag is chosen by the deploy, not by a registry lookup. Every stream targets
`develop`.

## Images

- `web` and `admin`: three-stage build (`deps` → `build` → `runner`), the
  runner copies only `.output`. The image does not typecheck: CI owns that.
  Runs as the `node` user of `node:26-alpine` (uid 1000); Nitro binds port
  3000 so no root capability is needed. The healthcheck hits `127.0.0.1`, not
  `localhost`: Nitro binds IPv4 and `localhost` resolves to `::1` first (#588).
- `api`: `dotnet publish` in the SDK image, runs on `aspnet:10.0` as the
  non-root `app` user (uid 1654) shipped by the base image. `curl` is
  installed for the `/healthz` healthcheck. `/home/app/crashes` is created and
  chowned before dropping privileges so a named volume mounted there inherits
  app ownership; a root-owned mount point would make the crash-file sink fail
  silently.
- `ingestor`: same shape on `runtime:10.0`. Liveness is a heartbeat file the
  worker rewrites every 30 s from a loop that runs for its whole lifetime,
  during a pass and during the wait between passes alike (#1229). The
  healthcheck rejects the container when the file is older than 300 s, ten
  beats: it is independent of how the job is scheduled and catches a wedged
  process in minutes. Progress, as opposed to liveness, is tracked by the
  `process_runs` heartbeat, which ages a stalled run out to Abandoned.
- `*.dev` variants run `dotnet watch` / `nuxt dev` with a long `start_period`
  (90–120s) so `condition: service_healthy` in `compose.dev.yaml` survives a
  cold start.

## Stacks

- `compose.yaml` is the local stack, `compose.dev.yaml` the hot-reload
  variant, `compose.preprod.yaml` and `compose.prod.yaml` the two deployed
  ones. Both deployed stacks run `Database__ApplyMigrationsOnStartup=false`
  and rely on the rollout to migrate.
- `STORAGE_DISK_CAPACITY_BYTES` is the volume size the admin storage forecast
  projects against (#925). Unset means no forecast at all, which the admin
  panel says explicitly instead of fitting a line to a guessed capacity.
- The `umami-purge` sidecar exists because self-hosted Umami has no retention
  for session replay and heatmap data (#1018): `session_replay` and
  `heatmap_event` are the heaviest tables it writes and grow unbounded. It is
  the Postgres image with the entrypoint replaced by a purge script on a loop,
  no server started.
- The ingestor's tuning knobs on prod (`ManualSeed__BatchSize`,
  `MatchIngestion__BatchSize`…) are documented in `docs/prod.md`.
