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
| `codeql.yml` | PRs (per language, by path), pushes to `develop`, weekly, manual | CodeQL security analysis of C#, JS/TS, Rust and the workflows themselves (below) |
| `sonar.yml` | PRs, pushes to `develop`, manual | SonarQube Cloud analysis with backend and frontend coverage; its quality gate is a required check (below) |
| `deploy-preprod.yml` | push to `develop`, manual | Preflight → version → publish images → roll out → tag |
| `deploy-prod.yml` | GitHub Release published | Preflight → publish images → roll out → verify the edge, and serve a held desktop app build the release catches up with |
| `build-images.yml` | called by both deploys | Builds and pushes the four images with the requested tags |
| `rollout.yml` | called by both deploys | Applies migrations over SSH, pre-pulls the images over SSH where enabled, then redeploys the Docker Manager project |
| `loadtest-preprod.yml` | manual | k6 load test against preprod from a GitHub runner; summary on the job page (`docs/load-testing.md`) |
| `desktop.yml` | PRs and `develop`/`master` pushes touching `desktop/`, `web/layers/common/` or `web/shared/`, each job on its own paths | fmt, clippy and tests of the desktop app's Rust crates; the macOS capture spike built and published as an artifact; the Windows capture helper built and smoke-tested on a Windows runner; the overlay smoke-tested on a Windows desktop; lint, typecheck, unit tests and static build of its Nuxt app (below) |
| `desktop-release.yml` | `develop` pushes touching `desktop/`, `web/layers/common/` or `web/shared/` (Markdown aside), or manual | Builds the desktop app for macOS and Windows: a preprod build every time, a production build on a version bump, served by production once it runs what the build reads (below) |
| `desktop-promote.yml` | manual, with a version | Serves a desktop production build on truemain.lol by hand — a rollback, or a held build (below) |

`.github/actions/migration-script` is the composite action every job that
needs the idempotent EF migration script goes through (`migrate-fresh` in CI,
`migrate` in the rollout), so the script that is validated is the script that
is deployed: same .NET SDK, same `dotnet-ef` version, same command. It also
prepends the session guard every apply runs under (#1631): `\set ON_ERROR_STOP
on`, so psql exits non-zero on the first error whatever flags the caller
passes, then `lock_timeout = '5s'` and `statement_timeout = '10min'`. EF writes
the script with a UTF-8 byte-order mark, which psql only skips at the very
start of a file; once the guard sits in front it would land mid-file and break
the first statement, so the action strips it.

`.github/scripts/resolve-preprod-version.sh` computes the `<base>-rc.<N>`
preprod version. It lives in its own file, with its own test
(`resolve-preprod-version.test.sh`), so the logic under test is the logic that
ships.

## CI: only the jobs the change can break

Every PR run starts with a `changes` job (`dorny/paths-filter`) that decides
which downstream jobs are worth a runner:

| Output | Paths | Gates |
| ------ | ----- | ----- |
| `backend` | `backend/**`, `global.json`, `web/shared/fixtures/performance-score-vectors.json` | backend build + unit + integration tests — the vectors are linked into the unit tests, which pin the C# score to the desktop app's port (#2075) |
| `data` | `backend/Data/**` | `migrate-fresh` |
| `web` | `web/**` | the site's frontend job |
| `admin` | `admin/**`, and from the site `web/layers/nitro/**`, `web/shared/**`, `web/layers/common/tsconfig.json` | the admin's frontend job |
| `backend_image` | `backend/*/Dockerfile`, `backend/.dockerignore`, `backend/Directory.*.props`, `backend/**/*.csproj` | API and Ingestor image builds |
| `web_image` | `web/Dockerfile`, `web/.dockerignore`, `web/package.json`, `web/package-lock.json`, `web/**/nuxt.config.ts` | the site's image build |
| `admin_image` | `admin/Dockerfile`, `admin/.dockerignore`, `admin/package.json`, `admin/package-lock.json`, `admin/nuxt.config.ts`, and from the site `web/.dockerignore`, `web/layers/nitro/**`, `web/shared/**`, `web/layers/common/tsconfig.json` | the admin's image build |
| `compose` | `compose*.yaml`, `.env*.example` | compose config validation |
| `scripts` | `.github/scripts/**`, `.github/file-size-baseline.txt`, `compose.preprod.yaml`, `compose.prod.yaml` | deploy-script tests, file sizes |
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

Both test projects are xunit v3 (#299), which runs on Microsoft Testing Platform
only — VSTest is not supported from xunit.v3 4.0. The repo-root `global.json`
switches `dotnet test` to that runner (`"test": {"runner":
"Microsoft.Testing.Platform"}`), which is why the steps pass the project with
`--project` instead of positionally. It sits at the root rather than under
`backend/` so the command also works when run from the repo root.

### Frontend

The npm bundled with Node 24 drifts with every patch release, and older npm
resolves sharp's platform-specific optional dependencies differently: a lock
file regenerated with the wrong one installs fine locally and breaks the web
build in CI. The `Pin npm` step installs `npm@11.13.0`, the version both lock
files are generated with (`npx npm@11.13.0 install` locally, see `CLAUDE.md`).

`nuxt typecheck` can pass on stale `.nuxt` types while `nuxt build` fails, so
the job runs typecheck, the vitest suite and a fresh build.

Neither of them notices a template tag that resolves to no component: Vue
renders it as an empty custom element and only warns at runtime, which is how
the admin Logs table vanished after a split left `LogsTable.vue` uncommitted.
The admin app sets `vueCompilerOptions.checkUnknownComponents`, so its
typecheck fails on such a tag.

Before those, `npm run lint` runs ESLint (#1440). Each app — `web/`, `admin/`
and the desktop app's `desktop/app` (in `desktop.yml`, below) — registers the
`@nuxt/eslint` module, which writes a flat config aware of the app's
auto-imports and its `app/` / `server/` / `shared/` split into
`.nuxt/eslint.config.mjs` during `nuxt prepare` (the `postinstall` that `npm
ci` runs). `eslint.config.mjs` extends it with the recommended preset, Vue's
rules and `eslint-plugin-vuejs-accessibility`. `web/` lints `web/layers/common`
and `web/layers/nitro` along with the rest of its tree; the desktop app and the
admin lint their own directory only, so each shared layer is linted once.

The severities are a ratchet, like the file sizes below: a rule nothing breaks
stays an error and fails the job, and each rule that was already broken when
the linter landed is listed in its app's `ratchet` and demoted to a warning,
which the job prints without failing. The accessibility rules are all warnings
for now, an owner's call on the noisiest plugin of the set. The config demotes a
rule's severity where `@nuxt/eslint` set it rather than overriding the rule
globally, so each rule keeps the files and options it was given. Once a rule's
last warning is fixed, the rule is taken off the list, so it fails the job again.

The web app's vitest config holds two projects (#1620), both run by the one
`npm run test` step: `unit`, the pure-function tests in a bare happy-dom
environment, and `nuxt`, the tests under `web/tests/nuxt/` that boot the real
Nuxt runtime through `@nuxt/test-utils` to mount components and exercise
hydration paths. They live under `web/**`, so the `web` path filter already
covers them.

### File sizes

The frontends had no linter before #1440, and neither ESLint nor the backend
analyzers have a file-length rule, so nothing stopped a controller from
reaching 869 lines or an admin page from carrying a 700-line template (#1429). `check-file-size.sh` is a **ratchet**
rather than a style rule: it lists every file over its limit — 500 lines for
backend `.cs`, 400 for `.vue`, 300 for frontend `.ts` — and diffs that list
against `.github/file-size-baseline.txt`. A new file over the limit or an
already-oversized one that grew fails the job; a file that shrank or dropped off
the list only prints a reminder to refresh the baseline with `--update`.

A plain threshold was not an option: 41 files were already over it when the
guardrail landed, so the check would have been red from the first run and
switched off. The baseline makes the existing debt explicit and lets it only
shrink, which is also how the epic that splits those files measures progress.

The baseline is in the `scripts` path filter alongside the scripts themselves, so a PR that touches nothing but
the baseline still runs the check — otherwise loosening an entry would be the one edit the guardrail never sees.
It also runs `deploy-scripts` for eight seconds, which is cheaper than a second filter output.

Generated code (migrations, the compiled model, build output), every test tree
and `web/server/utils/dev-api-mock.ts` are excluded — a long test file costs far
less to read than a long production file. The job runs its own test suite
(`check-file-size.test.sh`) first, because the check is shell walking a
directory tree and its failure mode is silently passing.

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

On a PR an image only builds when its packaging changes (the `*_image` filters
above, #2218): the Dockerfile and `.dockerignore`, the dependency manifests and
lock files, the .NET project files, and `nuxt.config.ts`, which holds the trace
rule the web image asserts below and the font providers. Every other change is
code the `backend` and `frontend` jobs already compile; building it again in an
image proved nothing they had not. The admin image also rebuilds on the site
paths its second context copies, since an import there reaching outside them
only fails inside the image. The `develop` push builds all four before preprod
deploys them, so a break the filter misses stops there, never at a release.

The admin image is the one built from two contexts. Its own is `./admin`; the
server layer it extends (`web/layers/nitro`, #1625) lives in the site's tree,
so `admin/Dockerfile` copies that layer, the site's `shared/` it imports and
the `web/layers/common/tsconfig.json` both of them extend from a second,
named context, `web=./web`. Every builder passes it — `build_contexts` in the
`changes` job's `images` matrix here and in `build-images.yml`'s,
`additional_contexts` in `compose.yaml` and `compose.dev.yaml` — and a build
without it fails on the first `COPY --from=web`. A named context rather than
the repo root as the context keeps the admin build from uploading the rest of
the repository, and `web/.dockerignore` still applies to it. The same files are
why the `admin` filter above includes those site paths: a change to any of them
changes what the admin builds.

### The OG renderer's wasm

`web/Dockerfile` asserts `harfbuzzjs/hb.wasm` is in the build output before
the runner stage copies it. Satori loads that file through a path it computes
at runtime, so nothing in the module graph points at it and Nitro's trace drops
it silently — the image builds, boots and serves every page, and only
`/_og/**` fails, with a 500 that shows up as a share card with no image. The
trace itself is fixed in `web/nuxt.config.ts` (`nitro.externals.traceInclude`);
the assertion is there so a regression fails the build instead of the next
tweet.

### No build downloads a font

The web image, the desktop bundle and their CI builds resolve every font from
the repo (#1106): Inter and Geist Mono from `web/layers/common/public/fonts/`
through a repo-local @nuxt/fonts provider, with every remote provider switched
off, and the share cards' static Inter from `web/public/fonts/og/`. Before, the
Docker build's `npm run build` downloaded the faces from the hashed
`fonts.gstatic.com` URLs Google's CSS API hands out; they rotate, and a cold
build failed on a 404 that a re-run cleared. The CI frontend job never saw it,
its runner's font cache was warm. A build log that mentions `fonts.google.com`,
`fonts.gstatic.com` or `Could not fetch from` means something brought a remote
provider back.

### The web server runs one worker per core

`web/nuxt.config.ts` builds with Nitro's `node-cluster` preset (#1579): the container's entry forks
`NITRO_CLUSTER_WORKERS` Node processes that share port 3000. With a single process, the 200-visitor preprod load
test left the web container's accept queue (backlog 511) overflowing about 130,000–150,000 times per run, and
the edge Caddy answered 502 after its 3 s dial timeout, while the API and the database had capacity to spare.
Server-side rendering is CPU work on one thread per process, so a second core did nothing for it.

- The worker count is set in the compose files, never left to the preset's default: inside a container
  `os.cpus()` reports the host's cores, not the container's share. Prod runs 3 of its 4 vCPU (the API, Postgres
  and the ingestors need the rest), the local stack 2, and preprod 1 because its host is shared (2026-09-16).
- Each worker has its own memory: the `/_ipx` byte cache (64 MB cap) and Nitro's in-memory cached functions are
  per worker, so a cache warmed by one worker is cold in the others, and the image cache's worst case is
  multiplied by the worker count.
- The admin portal keeps a single process; its traffic is one operator.

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

The same job then checks the one setting the env file can silently turn
against the compose file: `INGESTOR_JOB_MODE`. Both deployed compose files run
the ingestor as two lanes, `ingestor` on `${INGESTOR_JOB_MODE:-FetchWorkers}` and
`ingestor-aggregate` on `AggregateLane`, and the env file wins over that
default. Release `1.20.5` deployed green with a leftover `full` from a key
freeze: `ingestor` kept running the whole pipeline next to the new aggregate
container, and every aggregate step ran twice for about twenty hours (#1493).
`.github/scripts/check-job-mode.sh` therefore fails the preflight when the
compose file declares `ingestor-aggregate` and the env file sets the variable to
`Full` in any casing. An unset variable and the single-process freeze modes
`docs/riot-key-switch.md` relies on stay allowed, so the lever keeps working.
The env file reaches the script through an environment variable, never argv,
and only the offending mode is ever printed. `check-job-mode.test.sh` runs it
against the real deployed compose files in the `deploy-scripts` CI job, which
is why those two files are in the `scripts` path filter: renaming the aggregate
service must break the test, not the next deploy. The preflight checks out the
same ref the rollout deploys (the release tag on prod), so it reads the compose
file Docker Manager will.

### Rollout order and concurrency

`rollout.yml` runs `migrate` then `deploy`; the deploy depends on the
migration succeeding, so a failed script blocks the image roll
(`docs/production-migrations.md`). Where it is enabled, the deploy job's first
step pulls the images on the host before Docker Manager is asked for anything
(*Pre-pull before recreate*). The migration script is printed to the log
and uploaded as an artifact with 90-day retention, the only place the SQL is
visible before it applies.

### Migration over SSH

GitHub-hosted runners intermittently cannot reach the VPS: the connection
times out upstream of the host, nothing shows in its auth log, and the same job
re-run minutes later passes (#1568). Release `1.22.0` failed its migration step
that way. `apply-migration.sh` pipes the script into the host with a bounded
retry, on two schedules counted apart:

- **Connection failures**: four attempts, 15, 30 then 60 seconds apart, each
  with a 30-second `ConnectTimeout`. ssh exits 255 when it cannot connect or
  authenticate, and with the remote command's status otherwise, so only 255
  counts here.
- **Lock timeouts** (#1631): three attempts, 30 seconds apart. The script opens
  with `SET lock_timeout = '5s'` (`docs/production-migrations.md`), so a DDL
  that cannot get its lock fails instead of queueing traffic. The failure is
  recognised from what psql printed on stderr (`lock timeout` or SQLSTATE
  `55P03`), and since it runs in one transaction, a lost lock rolls everything
  back and the next attempt starts clean.

Any other failure of a script that reached `psql` is reported at once and never
re-run behind a retry. A retry after a connection dropped mid-script is safe
anyway: the script is idempotent, so whatever part of it had already applied is
skipped the second time. When a schedule runs out, nothing was applied and the
deploy job does not run: re-run the failed jobs, after finding the blocker in
the lock case.

The worst case is about six minutes: the four connection attempts take under
four (30 s timeouts plus 105 s of waits), and the two lock retries add 60 s of
waits plus a few seconds per attempt, since a script whose migrations are
already applied runs in seconds and a lost lock costs 5 s. That stays inside the
job's 15 minutes. If a pending migration runs long and the job is cancelled
mid-attempt, the session dies with its transaction and nothing is applied.
`.github/scripts/apply-migration.test.sh` pins the behaviour, both schedules
included, with an `ssh` stub and runs in the `deploy-scripts` CI job.

Each deploy workflow takes a **workflow-level** concurrency group with
`cancel-in-progress: false`. On preprod the rc counter is derived from the
tags already on the remote, so two runs resolving a version at once would land
on the same number; on prod two releases back to back would race for the
moving `:latest`. A running deploy is never cancelled, GitHub collapses the
pending queue to the newest run, and on preprod only a commit that actually
deployed gets its `-rc.N` tag (`tag` runs last). A group holds one running and
one pending run, and queuing a third cancels the pending one: that is why the
preprod load test keeps a group of its own (*Load test*).

### Pre-pull before recreate

Docker Manager **recreates before it pulls**: every container of the project
is stopped and removed, then the images are pulled, then the stack starts. A
pull that fails in between leaves every container `Created` and never started,
so a registry error is not a skipped deploy but an outage that lasts until
someone runs `docker compose up -d` by hand. Preprod was down twenty minutes
that way, behind a green pipeline, on a GHCR `denied` (#1089).

So the deploy job opens with `Pre-pull the images over SSH`: it asks the host
to pull the four application images at `IMAGE_TAG` (api, web, admin and the
ingestor both lanes run) and fails the run if the host cannot, before Docker
Manager is called, while the previous containers still serve.
`.github/scripts/prepull-images.sh` sends `pull <tag>` over the same CI key as
the migration, with the migration's bounded retry on connection failures only
(ssh exit 255: four attempts, 15, 30 then 60 seconds apart, 30-second
`ConnectTimeout`, *Migration over SSH*). A pull that ran and failed is
reported at once and not retried: nothing was touched, and re-running the
failed jobs is the retry. The step has its own ten-minute timeout, which is
why the deploy job's is 35.

What it buys is bounded by `pull_policy: always` on the application services:
Docker Manager still asks the registry once it has removed the containers.
After a pre-pull every layer is already local, so that second pull is a
manifest check per image instead of the whole download, and a tag the
registry refuses or does not have never reaches the recreate at all.
`.github/scripts/prepull-images.test.sh` pins the client with an `ssh` stub
and runs in the `deploy-scripts` CI job.

#### The CI key's forced command

The CI key is pinned in each host's `authorized_keys` to a forced command, so a
leaked key can never get a shell. Until #1089 that command was the migration
alone and any remote command was discarded. It is now
`.github/scripts/vps/ci-ssh-command.sh`, which reads the request sshd puts in
`SSH_ORIGINAL_COMMAND` and accepts exactly two:

- **No command, or `migrate`**: `exec`s the host's existing migration command
  (default `/usr/local/bin/apply-migration.sh`) with the client's stdin, so
  the `migrate` job, which sends no command and pipes SQL, runs exactly what it
  ran before.
- **`pull <tag>`**, the tag a 7 to 40 character lowercase git sha or a semver
  (`1.22.0`, `1.23.0-rc.4`): runs `docker compose pull api web admin ingestor`
  in the project directory with `IMAGE_TAG` set to the tag in an otherwise
  empty environment (`HOME` and `PATH` aside), then prints
  `ci-ssh-command: pulled <tag>`.

Anything else, a moving tag such as `latest` included, is refused with exit
64 and runs nothing. The tag is matched whole against the regex before it is
used, the request is never evaluated by a shell, and the project directory and
migration command are fixed in `authorized_keys`, never read from the client.
`ci-ssh-command.test.sh`, next to it, pins the accepted and refused requests
with `docker` and migration stubs and runs in the `deploy-scripts` CI job.

The client requires the success line, not just exit 0: a host still on the
previous forced command runs the migration with an empty stdin, which applies
nothing and can exit 0. Without the line that would read as a pull; with it, the
run fails and names the missing install.

**Installing it** is a host write, done by the owner on each VPS, once per
change to the script. `<project-dir>` is the environment's Docker Manager
project directory (the one holding its `docker-compose.yml` and `.env`,
`docs/preprod.md` and `docs/prod.md` say which), and `<ci-public-key>` the
public half of the environment's `*_SSH_KEY`:

```bash
# 1. Copy the script from the commit you are installing, root-owned, not writable by others.
install -o root -g root -m 755 ci-ssh-command.sh /usr/local/bin/ci-ssh-command.sh

# 2. Check both requests by hand before switching the key over.
SSH_ORIGINAL_COMMAND='pull <a-deployed-tag>' /usr/local/bin/ci-ssh-command.sh <project-dir>
SSH_ORIGINAL_COMMAND='ls' /usr/local/bin/ci-ssh-command.sh <project-dir>; echo "exit $?"   # refused, exit 64

# 3. In root's ~/.ssh/authorized_keys, on the CI key's line only, replace
#      command="/usr/local/bin/apply-migration.sh"
#    with
#      command="/usr/local/bin/ci-ssh-command.sh <project-dir>"
#    and keep the no-pty,no-port-forwarding,no-agent-forwarding,no-X11-forwarding options.
```

The existing migration script stays where it is: the new command delegates to
it. Rolling back is restoring the old `command=` value. The next deploy's
`migrate` job is the end-to-end check of the migration path; the pre-pull is
only exercised once it is enabled.

**Enabling the step**: it runs only when the repository variable
`PREPROD_PREPULL_IMAGES` (preprod) or `PROD_PREPULL_IMAGES` (prod) is `true`,
passed to `rollout.yml` as its `prepull` input. Unset, the step is skipped and
the rollout behaves as before, which is what lets this ship ahead of the host
install. Install the script on a host, then set that environment's variable
under *Settings → Secrets and variables → Actions → Variables*; the next deploy
of that environment pre-pulls. Turning the variable off is the rollback that
needs no host access.

### Verifying the rollout reached the VPS

`hostinger/deploy-on-vps` does not upload the compose file. It hands Docker
Manager a URL and Docker Manager **clones this repository from the VPS** to
read it, so the action returns as soon as the API accepts the request and
reports success without knowing whether anything rolled. Release `1.20.0`
shipped that way: seven green jobs, migrations applied, and prod left running
the previous release's images for forty minutes, i.e. the new schema against
the old binaries — the exact mismatch `preflight` exists to prevent, arriving
through the one door it does not watch (#1394).

`verify-rollout.sh` closes it. After the redeploy the job polls
`GET /vps/v1/virtual-machines/{id}/docker` — the same API the deploy posts to,
with the API key the job already holds — until every container built from an
image named in the compose file runs `IMAGE_TAG` and is healthy. It fails the
run otherwise, so a rollout that never reached the environment can no longer be
green.

**The migration's SSH channel cannot be reused for this.** The CI key is
pinned to a forced command in the host's `authorized_keys`
(`command="…",no-pty`), so it may run the migration and, since #1089, an image
pull, and nothing else (*The CI key's forced command*): a `docker ps` is
refused. That is the right shape for a CI credential and it is deliberately
kept narrow, so the deploy job's evidence has to come from somewhere the key
does not gate. The Hostinger API is that place, it needs no new secret,
and it reports the live container list rather than what the compose file
intends.

The expected images are **read from the compose file** rather than listed in
the workflow, so adding a service extends the check for free, and a compose
file naming none of our images is a hard error instead of a vacuous pass. The
grain is the **image reference**, not the service name: both environments run
two ingestor lanes from one image (#1374, #1490), and rolling only one of them
is a partial deploy no service-name check would catch. Health is required as well as
the tag, because a container that starts on the right image and crash-loops has
not deployed either; a container with no healthcheck of its own reports an
empty health and is not read as unhealthy, while `starting` is treated as
not-ready and polled through, which is what lets a slow roll pass.

An API error is fatal rather than an empty container list: "the API did not
answer" and "the project runs nothing" are different facts, and reading the
first as the second is how a check reports a deploy that never happened as
merely slow.

The wait is ten minutes at fifteen-second intervals, which is why the deploy
job's own timeout is larger than the migration's.
`.github/scripts/verify-rollout.test.sh` pins the behaviour against fixed API
responses and runs in the `deploy-scripts` CI job.

### What the deploy writes on the VPS

Exactly two things, on both environments: the **compose file**, which Docker
Manager reads from this repository at the deployed commit and stores as the
project's `docker-compose.yml`, and the project **`.env`**, overwritten from
the `PROD_ENV_FILE` / `PREPROD_ENV_FILE` secret plus `IMAGE_TAG` and
`APP_VERSION`. Nothing else in the repository reaches the host: not a sibling
file a compose file would bind-mount with a relative path, not a script, not a
config. The `migrate` job writes nothing either, it pipes SQL into a fixed
command on the host, and the pre-pull only adds images to the host's image
store. The CI key's forced command (*Pre-pull before recreate*) is the one
repository file on the host, installed by hand rather than by the deploy.

So **every piece of configuration a deployed stack needs lives inside its
compose file**: the edge Caddyfile and maintenance page of both edges
(`configs.edge_caddyfile` and `configs.maintenance_page` in
`compose.*.edge.yaml`), preprod's Umami proxy and Umami's replay cleanup
script are top-level `configs:` with inline `content`. A relative bind mount
in `compose.prod.yaml` or `compose.preprod.yaml` is a file someone has to copy
by hand, and it drifts silently: prod's edge ran a hand-copied `Caddyfile`
while two released edge changes (#1583, #1696) never reached it (#1598).
Only named volumes hold state on the host.

Inline `content` is interpolated like the rest of the compose file, so a
literal `$` in it is written `$$`.

### Verifying the edge

`verify-rollout.sh` proves the containers run the released images; it cannot
see what the edge does with their responses. The `edge` job of
`deploy-prod.yml` runs `verify-edge.sh` after the rollout and asks the live
public site, with a client that offers `zstd, gzip`, for its landing page and a
JSON route (`/api/static/versions`, large enough for Caddy to compress). It
fails the run unless both answer 200 with a `Content-Encoding` of `zstd` or
`gzip`, and the page carries `Strict-Transport-Security` and
`Content-Security-Policy` — the directives that were shipped and silently
absent (#1598). It runs after the `edge-rollout` job and polls for five
minutes, because a release that changes the edge recreates Caddy, which may
still be starting when that job's check passes.

It only runs on prod: preprod's origins are operator values kept out of the
repository, and its edge is plain HTTP without HSTS or CSP by design
(`docs/preprod.md`). `.github/scripts/verify-edge.test.sh` pins the checks
against fixed headers and runs in the `deploy-scripts` CI job.

### The edge is its own project

Docker Manager recreates every container of a project on each deploy — the
databases and the edge Caddy included, whatever changed. With Caddy in the app
stack, every release left the public site answering nothing for 40 to 60 seconds,
and nothing in the stack could serve a maintenance page while the stack itself
was down (#2179).

So each environment runs **two Docker Manager projects**: the app stack
(`truemain` from `compose.prod.yaml`, `truemain-preprod` from
`compose.preprod.yaml`) and its edge (`truemain-edge` from
`compose.prod.edge.yaml`, `truemain-preprod-edge` from
`compose.preprod.edge.yaml`), Caddy alone. The app stack owns a dedicated
network (`truemain_edge` / `truemain_preprod_edge`) that web, admin and (prod)
Umami join; the edge joins it as `external`, so it reaches the services it
proxies and nothing else. That ownership sets the order: the app rolls first and
never depends on the edge; the edge needs the network the app created.

The reusable `edge-rollout.yml` runs after `rollout.yml` in both deploy
workflows. Redeploying the edge recreates Caddy, so it only happens when it
changes something: `edge-drift.sh` asks the Docker Manager API for the project
list and the compose file the edge project holds, and deploys when the project
is missing, when a container of it is not running, or when the file differs
from the repository's at the deployed commit — never because the API failed to
answer. `verify-edge-running.sh` then polls until every edge container runs:
the edge builds no image of ours, so `verify-rollout.sh` has nothing to assert
on it. Both scripts are pinned by `*.test.sh` in the `deploy-scripts` job.

The maintenance page is duplicated in the two edge files, because each deploy
reads one file; the `compose-config` job fails when the two copies differ.

### Preprod versioning

`version` resolves `<base>-rc.<N>` from a full-history checkout: the base is
the next minor after the latest bare `MAJOR.MINOR.PATCH` tag (or the
`PREPROD_VERSION_BASE` repository variable when the next release is known to
be a major), N is the highest existing counter on that base plus one. Git's
version sort ranks `1.20.0-rc.4` above `1.20.0`, so anything reading "the
latest release" must filter to bare `MAJOR.MINOR.PATCH`. `-rc.` and `.` are
legal in a Docker reference, `+` is not, which is why the version is a semver
prerelease and not build metadata.

## Desktop app checks

The desktop app renders the site's shared pages from `web/layers/common` and
reads the site's `web/shared` types (#1732), so a change on the site's side can
break the app without touching `desktop/`. `desktop.yml` therefore also runs on
those two paths, and its `Nuxt app` job lints the app (`npm run lint`, set up
as on the site, see Frontend), typechecks it and builds its static
bundle (`npm run generate`, what `tauri build` runs) with only the app's own
dependencies installed — the same conditions as `desktop-release.yml`, where a
shared file importing a package the app lacks would otherwise fail first.
Between the two it runs the app's vitest suite (`npm run test`, #1724): pure
tests of the dashboard's rules — each game's LP gain from the locally noted
standings, recent form against the player's average — in a bare happy-dom
environment with no Nuxt runtime. `npm ci` runs `nuxt prepare` (the app's
`postinstall`, as on `web/` and `admin/`) because the tests' TypeScript
transform reads the generated `.nuxt` tsconfig.

The `macOS capture spike` job builds the game-recording spike (#1745): the
Swift capture helper (`desktop/capture/macos`, ScreenCaptureKit, so only a Mac
can compile it — the Linux job never sees it) and the Rust runner
(`capture-spike`, which drives it through the `capture-helper` crate the app
uses too), both universal (arm64 + x86_64) and ad-hoc signed like the
app's own build. It smoke-runs each binary for its usage error, then uploads
them as one `.tar.gz` — tarred because an artifact's zip drops the executable
bit — so the spike can be run on a Mac without a toolchain.

The `Windows capture helper and shell` job (#1797) runs clippy and the tests
of the Windows helper (`desktop/capture/windows`, a workspace crate whose code
is all `cfg(windows)` — the Linux job only formats it and tests its
platform-free parts) and of `capture-helper` on a Windows runner, builds and
runs the Tauri shell's tests on Windows (its `cfg(windows)` paths are compiled
nowhere else before a release, and no other job can build the shell to test it:
Linux lacks its GTK libraries), then builds the helper and runs
`desktop/capture/windows/smoke.ps1`: a window that repaints itself is recorded
for eight seconds, a clip is cut out of the video and two thumbnails are
taken, each answer of the helper checked. The runner has no GPU, so frames are
converted on the CPU and encoded in software there: the job proves the
protocol, the capture and the files, not a League game or the GPU path. The video, clip and
thumbnails are uploaded as an artifact.

The `Windows overlay` job (#1806) builds the app as `tauri build --no-bundle`
does for a release and runs `desktop/tools/overlay-smoke-windows.ps1` on the
runner's desktop. The app replays `fixtures/ranked-game.jsonl`
(`TRUEMAIN_LCU_REPLAY`). A stand-in compiled with the .NET Framework's `csc`,
a full-screen window of the game's class (`RiotWindowClass`) and process name,
takes the game's place in front. Each step reads the panels' windows: shown,
extended styles, the window a click at their centre reaches. Each step also
screenshots the desktop with `CAPTUREBLT`, since layered windows are missing
from a plain copy, and checks that the panels' pixels are drawn over the
stand-in's flat colour. The steps are:

- the game in front;
- TAB held;
- Alt+Shift+O twice;
- another app in front;
- a window of the game's class alone;
- the preview, opened through UI Automation on the game page's buttons, with a
  panel dragged by the mouse and its place saved and kept;
- the loading screen: the app restarted on the same tape with its first game
  reading made into the loading screen's (ten players, no `GameStart`, clock
  at zero), and no panel shows over the stand-in.

The build reads preprod, like the beta (`DESKTOP_BETA_SITE_URL`), so the panels
get their data from the API that has their endpoints. Without the secret, as on
a fork's PR, it reads production, which may not serve the newest endpoints yet.
The script turns the app's usage counts off first, the way its menu does, so a
run is not counted as an install. It wipes the site's address from the app's
log before the upload, and a step that runs even when the test was cut short
(`-RedactOnly`) does it again. The runner has no GPU, so Windows draws no rounded
corners. The screenshots, a JSON per step and the app's log
are uploaded as an artifact.

A `changes` job gates each of these jobs on what it compiles (#2218), as in
`ci.yml`: the `Rust crates` job on the workspace manifests, `desktop/crates/`
and `desktop/capture/windows/`; the Windows shell job on those and
`desktop/src-tauri/`; the macOS spike on those and `desktop/capture/macos/`;
the Windows overlay on the shell's paths and `desktop/app/`, its smoke script
and the fixtures it replays; the `Nuxt app` job on `desktop/app/` and the
site's two shared paths. A change to the site's shared layer therefore runs the
`Nuxt app` job alone: the Windows and macOS jobs build Rust, which it never
reaches. A change to `desktop.yml`, a push and a draft marked ready behave as
in `ci.yml`.

## Desktop releases

The desktop companion has its own version, never the site's release flow (#1719,
#1772): `version` in `desktop/src-tauri/tauri.conf.json`, bumped by hand in a PR.
Since #1799 nothing else is by hand. `desktop-release.yml` runs on every
`develop` push that touches the app — `desktop/`, and the site's shared pages and
types (`web/layers/common/`, `web/shared/`), Markdown aside — and its `Versions to build`
job (`.github/scripts/desktop-version.sh`, tested by `desktop-version.test.sh` in
the `deploy-scripts` CI job)
decides what it builds:

- **always, a preprod build**, versioned `X.Y.Z-beta.N` with the run number and
  published as the pre-release `desktop-vX.Y.Z-beta.N`. The Tauri updater
  installs a build only when its version is strictly greater than the installed
  one, so a commit SHA — unique but not ordered — cannot be the version; it is in
  the release's notes instead. `X.Y.Z` is the version being worked towards: the
  configured one while its production build does not exist, the next patch once
  it does, because semver ranks `0.4.0-beta.N` below `0.4.0`. The beta's version
  is written in the preprod flavour's config overlay; the file keeps the bumped
  version. Only the last ten betas are kept — release and tag — so the list does
  not grow by several releases a day.
- **on a version bump, a production build** — when no `desktop-vX.Y.Z` release
  exists yet, so a failed bump build is rebuilt by the next run, or by *Run
  workflow* on `develop`. It is created as a **draft** `desktop-vX.Y.Z`, then
  served at once if production already runs what it reads (below).

The version must be `MAJOR.MINOR.PATCH`, and the workflow refuses any branch but
`develop`. A re-run of the same run reuses its run number: the beta is then
skipped with a notice, so a new beta takes a new run. GitHub keeps one run
pending behind the one building and cancels the older pending ones, so a burst of
merges builds the first and the last; a bump in a cancelled run is built by the
next one, since the bump is read from the file and not from the push.

Each flavour is a separate build (#1779): a build reads the API, opens the pages
and polls the update feed of the site it was built for (`TRUEMAIN_SITE_URL`,
`desktop/src-tauri/src/site.rs`), so the app downloaded from preprod must be a
different binary from the one downloaded from truemain.lol.

| flavour | site | files | update feed | app |
| --- | --- | --- | --- | --- |
| production | `https://truemain.lol` | `truemain.dmg`, `truemain.exe` | `latest.json` | *TrueMain*, `gg.truemain.desktop` |
| preprod | the `DESKTOP_BETA_SITE_URL` secret | `truemain-<version>.dmg`, `truemain-<version>.exe` | `latest-beta.json` | *TrueMain Beta*, `gg.truemain.desktop.beta` |

The browser saves an installer under its asset name, so a preprod download says
its version and a production one does not. The preprod flavour is the
production config plus an overlay the job writes (`tauri build --config`): its
version, its own name and identifier, so the two install side by side; its own
update feed, preprod's `/api/desktop/latest.json`; and, while preprod has no TLS,
`dangerousInsecureTransportProtocol` — the updater still verifies every archive
against the signing key, the flag only lets it read the feed over HTTP. The
preprod origin is a secret because the repository is public and never names its
hosts; the job fails when it is missing or is not a bare origin. When a run builds
both flavours, they build in the same job, one after the other, so the second
reuses the first's compiled dependencies. The Windows installer takes
`X.Y.Z.0` as its numeric file version: NSIS drops the `-beta.N`, which the app
itself keeps.

It builds on the two platforms the app supports, each on its own runner:

- **macOS** (`macos-15`): one universal binary (`--target universal-apple-darwin`,
  both Rust targets installed), bundled as a `.dmg` and as the `.app.tar.gz` the
  updater installs. Ad-hoc signed (`bundle.macOS.signingIdentity: "-"`), which is
  what lets an unsigned app run on Apple Silicon at all; not notarised. The Swift
  capture helper (#1744) is built universal first, ad-hoc signed on its own and
  copied to `src-tauri/binaries/` (gitignored), from where `bundle.macOS.files`
  puts it in `Contents/MacOS` beside the app's binary — signed before bundling,
  so the app's seal covers a signed executable. The job then checks the helper
  is in the bundle and that the bundle's signature verifies.
- **Windows** (`windows-2025`): the NSIS `-setup.exe`, which is also what the
  updater installs. Not code-signed. The capture helper (#1797) is built first
  and copied to `src-tauri/binaries/` under its target-triple name; the job
  then adds it to `flavor.json` as `bundle.externalBin`, which installs it
  beside `TrueMain.exe`. It is not in `tauri.conf.json` because a sidecar
  listed there must exist for every build, `tauri dev` included.

Each update artifact is signed with the updater key, the
`TAURI_SIGNING_PRIVATE_KEY` repository secret; the public half is
`plugins.updater.pubkey` in the app's config. The key has no password, so
`TAURI_SIGNING_PRIVATE_KEY_PASSWORD` is left unset and reaches the build empty;
a key rotated to one with a password needs that secret too. Losing the private
key means the installed apps can no longer be updated: every tester would have
to reinstall a build carrying a new public key. Each publish job writes its
flavour's updater manifest (`.github/scripts/desktop-manifest.sh`, both macOS
keys pointing at the one universal archive) and creates its release:
`latest-beta.json` on the beta pre-release, `latest.json` on the production
draft. Neither carries GitHub's "Latest" badge, which stays the site's.

`deploy-prod.yml` runs on every *published* release, so its `preflight` job
skips `desktop-v*` tags and the whole deploy with it. A release created or
edited by a workflow's own `GITHUB_TOKEN` triggers no workflow anyway; the guard
covers one published by hand.

### Production follows production

The production app calls the production API, and the pages it shares with the
site (#1732) as they were on `develop` when it was built — the champion
directory reads `/champions/directory`, for one. Served before production runs
the same endpoints, those pages would fail in every installed app. So a
production build stays a draft — invisible to the site and to the updater —
until `.github/scripts/desktop-held.sh` (tested by `desktop-held.test.sh`, same CI
job) finds
the site release running in production **aligned** with it:

- the release contains the bump commit (it was cut from `develop` after the
  merge), or
- the two do not differ on what the app reads: `web/`, and the API behind it
  (`backend/Api`, `backend/Core`, `backend/Data`).

The check runs twice. Right after the bump's build, `desktop-release.yml` runs it
against the tag of the last successful `Deploy Prod` run — a bump that changed
nothing the site serves goes out at once. Then `deploy-prod.yml`'s `desktop` job
runs it after every site release's rollout, so a bump that needed new endpoints
goes out with the release that deploys them. The newest aligned draft is
promoted; one older than the stable release never is.

A promotion (`.github/scripts/desktop-promote.sh`) turns the release into a full
release after checking it carries the production flavour's installers and
manifest, moves any previous stable desktop release back to pre-release, and
deletes held drafts older than it. Exactly one release is stable at a time.
`desktop-promote.yml` runs the same script by hand, with a version: to serve a
held build early, or to roll back — promoting an older version serves it again
on the download page, though the updater never downgrades an installed app. A
rolled-back version is a pre-release, not a draft, so no site release re-promotes
it.

The site reads the releases rather than the app linking to GitHub:
`/api/desktop/download/{platform}` and the updater's feed
`/api/desktop/latest.json` resolve a `desktop-v*` release, so a new app version
needs no site deploy. Which one depends on the site's channel,
`NUXT_DESKTOP_CHANNEL` in the compose files:

- **`beta`** (preprod): the newest release carrying the preprod flavour's
  manifest (`latest-beta.json`) — every app change merged to `develop`, within
  five minutes — and its installers (`truemain-<version>.*`).
- **`stable`** (production, and the default for anything else): the release that
  is not a pre-release, and its production flavour (`truemain.*`, `latest.json`).
  Until one is served, production offers no app.

GitHub lists releases newest first, a hundred a page: the site reads further
pages (five at most) until its channel's release turns up, since the site's own
releases and the betas push the stable one down.

A channel never serves the other's flavour, so an app never leaves the site it
was downloaded from, updates included. Each flavour's updater polls its own
site (`plugins.updater.endpoints`): the preprod app is offered every new build,
the production app every version production has caught up with.

## Load test

`loadtest-preprod.yml` runs `loadtest/k6/run.js` against preprod (`docs/load-testing.md`, #1559).

- It is manual only. A load test is an event someone decides on — the rate limit may need raising for it —
  not something a push should trigger.
- It keeps its own concurrency group, `preprod-loadtest`, and coordinates with the deploy by looking at the
  other workflow's runs instead (#1566). Sharing `preprod-pipeline` would have made a dispatched test the
  group's pending run and silently cancelled a deploy already queued there.
  - The test's first step refuses to start while a `deploy-preprod.yml` run is queued or running.
  - The deploy's `preflight` waits, up to 45 minutes, while a load test is queued or running, so it never
    recreates the containers under a test. Both read the runs with the workflow token (`actions: read`).
- The target is built from the existing `PREPROD_SSH_HOST` secret. The repository is public, so the host is
  never written into the workflow, and a guard step deletes the output and fails the job if any file in it
  names the host; only then is the summary appended to the job page and uploaded. Secrets are masked in the
  logs, but not in artifacts.
- k6 is pinned (`grafana/setup-k6-action` with an explicit `k6-version`), so two runs weeks apart differ by
  the site, not by the tool.
- Before k6 starts, the runner checks that preprod answers, five times 20 seconds apart. Some runner addresses
  never reach the host (#1568), and a clear error telling the operator to re-dispatch beats a k6 run whose
  every request times out.
- The k6 step records its exit code instead of failing, so a run that crossed a threshold (exit 99) still
  publishes its summary; the last step fails the job afterwards.
- The browser VUs use the Chrome preinstalled on the runner image, found on the `PATH` by a step that fails
  with a clear error if it is gone, and run with `no-sandbox`, as headless Chrome on a CI runner needs.
- k6's own log goes to `out/k6.log`, not to the job log: the browser logs the URLs it loads, and a job log is
  public. The step replaces the host in that file before the guard runs, so the guard still refuses any other
  output that names it.

## Claude review

`claude-review.yml` posts blocking points only (#2218): a clean PR gets a bare
approval with no body and no inline comment; otherwise each point is an inline
comment prefixed `BLOCKING:` and the review requests changes. Suggestions, nits
and code smells are never posted, so a review that says anything is one to act
on. It reviews along two axes kept apart in its body, so one
cannot mask the other: **Spec** (does the diff do what the linked issue's
`Scope`/`Acceptance` ask, read with `gh issue view`; the PR body when there is
no issue) and **Standards** (the repository's written rules). The prompt defines blocking narrowly (bugs, security, data
loss, regressions, missing tests, project rules CI cannot catch) and forbids
flagging versions or APIs from memory, because the repository regularly runs
tooling newer than the model's training data. Dependabot is in `allowed_bots`
so its PRs get a verdict too; the prompt tells the reviewer what to check on a
dependency bump. Editing the workflow file makes its own check fail on that
PR; it passes again post-merge. Verdicts trail one commit behind pushes: check
the review's `commit_id` against the head SHA before acting on it.

## SonarQube Cloud

`sonar.yml` sends the repo to SonarQube Cloud (#2051), free because the repo is
public: duplication, cognitive complexity, coverage over time and a quality gate
on the PR's new code, none of which the analyzers, ESLint or the file-size
ratchet measure. One project covers the whole monorepo, `develop` as its main
branch.

It is a workflow of its own rather than a job in `ci.yml`. The .NET scanner
turns `TreatWarningsAsErrors` off for the build it wraps, so that build cannot
be the `backend` job's: the analyzer gate would silently stop failing. Its own
build costs runner minutes, not wall time, since it runs beside CI.

- `plan` decides whether there is anything to analyse: nothing when
  `SONAR_TOKEN` is not readable (Dependabot and fork PRs get no secrets),
  when a PR only touches documentation (Markdown, `docs/`, `.claude/`), or
  when it targets `master` — a release PR's "new code" is the whole
  develop-vs-master diff, every line of it already analysed by the `develop`
  push that brought it, and the gate cannot be tuned, so it would only ever
  restate the coverage of code merged before the gate existed (#2061) — each
  with a notice instead of a red check. It filters inside the workflow, not
  with `on: paths`, so the required check below exists on every PR. It also
  plans what the analysis needs (#2218): the frontend coverage of the apps the
  PR touches (`web` on `web/**`, `admin` on its own tree and the site paths it
  extends, the desktop app on `desktop/app/**`), and the backend test suites
  only when the backend changed (`ci.yml`'s `backend` paths). A PR analysis
  rates new code only, so an area the PR leaves alone needs no coverage report.
  Pushes to `develop` and a change to `sonar.yml` plan everything. The build
  itself always runs: the .NET scanner reads the TypeScript and Vue code
  through it, so skipping it would mean a second scanner and a different set
  of analysed files from one PR to the next.
- `frontend-coverage` runs each app's vitest suite with v8 coverage (lcov) and
  rewrites the app-relative `SF:` paths to repo-relative ones. It installs with
  `--ignore-scripts` and runs only the apps' own `postinstall` (`nuxt prepare`),
  so no dependency's lifecycle script runs in a job that sits next to the token. The desktop app's
  run only covers its own tree; the shared layer's coverage comes from `web`.
- `analysis` wraps the Release build and, when planned, both test suites between
  `dotnet-sonarscanner begin` and `end`. Coverage comes from
  `Microsoft.Testing.Extensions.CodeCoverage` (`dotnet test --coverage`, XML
  format): `dotnet-coverage collect` cannot instrument the Microsoft Testing
  Platform test host. The scanner, pinned in `backend/.config/dotnet-tools.json`
  where Dependabot's NuGet watch sees it, also analyses every non-MSBuild file
  under the repo (`sonar.scanner.scanAll`), which is how the TypeScript and Vue
  code is read without a second scanner. Generated code (EF migrations, the
  compiled model, `.nuxt`), tests, the dev API mock and `.claude/` are
  excluded: the agent's skills and function-hook mods under `.claude/skills`
  never ship, their tests run under `claude plugin test` (bun, outside CI), and
  their `tsconfig.json` extends a file the Claude Code engine generates on load
  — analysed, they read as uncovered new code and fail the gate (#2068).

The quality gate **blocks the merge** (#2052). On a PR, `analysis` passes
`sonar.qualitygate.wait=true`, so its `end` step fails when the gate is red, and
the last job, `Quality gate`, is the status check `develop` requires: it fails
when any job before it failed and passes when they succeeded or were skipped.
The check the SonarCloud app posts on the PR carries the same verdict with the
failing conditions, but it is not the required one — it is absent whenever the
analysis is skipped, which would leave documentation and Dependabot PRs waiting
forever. Pushes to `develop` never wait for the gate: they only refresh the
baseline.

The gate is SonarQube Cloud's built-in "Sonar way" — the free plan allows no
other — on the PR's new code: ratings A for reliability, security and
maintainability, 80 % coverage, at most 3 % duplication, every security hotspot
reviewed. Its thresholds cannot be changed, so tuning means choosing what
reaches it:

- `sonar.cs.roslyn.ignoreIssues=true`: the build already runs the .NET,
  Threading and Roslynator analyzers with the severities `analyzers.globalconfig`
  sets (#294). Without it the scanner re-imports every one of their suggestions
  as a Sonar issue — about 12,000 on the first run, 10,000 of them `IDE0008` —
  so Sonar only reports its own rules.
- Vue components count toward coverage, an owner's call: a PR that adds
  component logic brings its tests (`web/tests/nuxt/` for the site). The first
  run measured the frontends' `app/` trees at 22–26 % and the backend and
  `web/shared` around 90 %.
- A new security hotspot is reviewed on SonarQube Cloud (Security Hotspots →
  Safe / Fixed) by the owner; the gate turns green on the next run.

Automatic Analysis must stay off in the SonarQube Cloud project: the CI-based
analysis refuses to run while it is on.

## CodeQL

`codeql.yml` runs GitHub's CodeQL security analysis (#2057), free on a public
repo; alerts land in the repository's Security tab. It replaced the "default
setup" switched on from the repository settings, which analysed every language
it detected on every PR with no path filter: a CI-only PR paid for C#, JS/TS,
Rust, Actions and a Swift build on a macOS runner that took over a quarter of an
hour. Default setup must stay off: GitHub rejects an advanced workflow's
uploads while it is on.

A `changes` job, the same shape as `ci.yml`'s, picks the languages a PR can
affect: `csharp` for `backend/`, `javascript-typescript` for `web/`, `admin/`
and `desktop/app/`, `rust` for the desktop crates, `actions` for `.github/`.
Pushes to `develop`, the Monday scan and a change to `codeql.yml` itself
analyse all four, so `develop` always holds a complete baseline; a PR whose
language was skipped is compared against it. Every language runs with
`build-mode: none`, so no job restores or compiles anything. Tests are outside
the analysis (`paths-ignore`), as they are for Sonar: they never ship, and
their string fixtures read as unsanitised HTML.

Swift is not analysed: its only code is the macOS capture helper and a local
probe script, and analysing it needs a macOS runner and a full build.

## Third-party actions are pinned by commit

Every action that GitHub does not publish (`actions/*`, `github/*`) is pinned
to a full commit SHA rather than a tag (#2052): a tag can be moved to malicious
code after review, as `tj-actions/changed-files` was in 2025, a commit cannot.
Dependabot's `github-actions` stream still bumps them, to the commit of the new
release. `dtolnay/rust-toolchain` is the one exception to that: it publishes no
releases and is used through its `stable` branch, so its pin is moved by hand,
and its `toolchain: stable` input says which toolchain the branch name used to
select. SonarQube Cloud flags an unpinned third-party action as a vulnerability
(`githubactions:S7637`), which fails the gate.

## Dependabot

One weekly PR per ecosystem groups every minor and patch update; majors stay
individual so a breaking change is reviewed on its own. `docker-compose`
watches the third-party images pinned in the stacks (postgres, pgbouncer,
mongo, umami, caddy), which no Dockerfile references; our own
`ghcr.io/ilyanfraimbault/truemain-*` images are ignored there because their
tag is chosen by the deploy, not by a registry lookup. Every stream targets
`develop`.

The NuGet stream has one extra group, `ef-core`, matching every
`Microsoft.EntityFrameworkCore*` package at any update type and declared before
`minor-and-patch` (Dependabot assigns a dependency to the first group that matches).
The runtime, `Relational` and `Design` packages must move in lockstep: `Design`
drives `dotnet ef` and the compiled-model generator, and a `Relational` pin below
the one the Npgsql provider pulls in is silently lifted anyway, so a split bump
leaves a misleading version on the page (#1637).

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
- **The app containers run with `init: true`** (2026-09-16). Docker's health check
  starts a process inside the container; when it ends, it is reparented to PID 1,
  and neither Node nor .NET reaps a child it never spawned. With a probe every
  10s the leftovers pile up: 103 `[wget] <defunct>` in prod's web container after
  four days, 46 on preprod plus one each on admin and api. `init: true` puts
  Docker's `tini` at PID 1, which reaps them; the app still receives signals and
  stops the same way.
- **Every probe is generous on time, strict on meaning** (2026-09-16). Timeouts
  are 20s and start periods 90s across the images and the data stores, and the
  retry counts are doubled. The preprod host is CPU-limited by the provider when
  the stack starts, and a `mongosh` ping that normally answers in milliseconds
  took 52s there: the 5s timeouts turned a healthy Mongo into an unhealthy
  dependency and Docker Manager abandoned the deploy (`dependency failed to
  start`). What each probe *checks* is unchanged — the ingestor still fails on a
  heartbeat older than 300s — only the patience is.

## Stacks

- `compose.yaml` is the local stack, `compose.dev.yaml` the hot-reload
  variant, `compose.preprod.yaml` and `compose.prod.yaml` the two deployed
  ones. Both deployed stacks run `Database__ApplyMigrationsOnStartup=false`
  and rely on the rollout to migrate.
- PgBouncer reads a `pgbouncer.ini` written inline under `configs:` in every
  stack, not the image's env-generated one (#1631). The env vars can only
  describe one database entry, and the API needs its own: `${POSTGRES_DB}_api`
  points at the same database with a `connect_query` that sets
  `statement_timeout`, so the API's server connections carry it and the
  ingestors' do not. The image still writes `userlist.txt` from `DB_USER`,
  `DB_PASSWORD` and `AUTH_TYPE`, the only env vars it keeps. Each database
  entry also carries its share of the server connections (`pool_size`,
  `max_db_connections`), and every service's `Maximum Pool Size` in its
  connection string is sized to that share: the per-service budget, which
  adds up to `max_user_connections`, is in `docs/prod.md` (*Connection pools*,
  #1632), with the other values.
- Both deployed environments put Caddy in front of web and admin, in an edge
  project of its own (`compose.*.edge.yaml`, *The edge is its own project*,
  #2179) with its Caddyfile inline under `configs.edge_caddyfile`, because the
  deploy ships the compose file alone (*What the deploy writes on the VPS*; prod
  in `docs/prod.md`, #1598; preprod's plain-HTTP edge in `docs/preprod.md`,
  #1558). The preprod stack requires `PREPROD_SITE_URL` and
  `PREPROD_ADMIN_URL`, which is why the compose-config job sets both.
- Both deployed stacks publish the API's port on the loopback interface only
  (`127.0.0.1:8080:8080` on prod, `127.0.0.1:8081:8080` on preprod). Nothing
  external needs it: web and admin reach the API over the internal network as
  `http://api:8080`, and Caddy proxies to web/admin, never to the API directly.
  Binding to loopback keeps host-local diagnostics (`curl localhost:8080` over
  SSH) working while taking the API off the public interface, so the edge is
  the only public entry and app-layer defences no longer lean on the host
  firewall alone.
- pgAdmin was removed from every stack: the DB console it exposed added a
  password-only, internet-reachable surface on preprod (`0.0.0.0:5051`) for no
  benefit over `psql` on the loopback Postgres port. Its `PGADMIN_*` env vars
  are gone from the `.env` examples too.
- Both ingestor lanes carry `stop_grace_period: 120s` on the deployed stacks
  (#1513). Docker's default is 10 s, and a pass runs for many minutes, so every
  redeploy SIGKILLed the work in flight: the in-flight transaction rolled back and
  the run was left `Running` for the next boot to reconcile. The work is
  cooperative, so what it lacked was the time to unwind. The host's own
  `ShutdownTimeout` (100 s, set in `Ingestor/Program.cs`) sits below the grace
  period so the runtime finishes on its own terms rather than being killed
  mid-finalisation; a cancelled pass normally stops in seconds, and the window is
  only a ceiling. A run stopped this way is recorded `Cancelled` — the counterpart
  of `Abandoned`, which means the host died instead of stopping — and the claims
  the dead incarnation held are released at the next boot rather than a full
  `MatchIngestion:ClaimLeaseMinutes` later.
- `STORAGE_DISK_CAPACITY_BYTES` is the volume size the admin storage forecast
  projects against (#925). Unset means no forecast at all, which the admin
  panel says explicitly instead of fitting a line to a guessed capacity.
- The `umami-replay-cleanup` sidecar exists because self-hosted Umami has no retention
  for session replay and heatmap data (#1018): `session_replay` and
  `heatmap_event` are the heaviest tables it writes and grow unbounded. It is
  the Postgres image with the entrypoint replaced by a purge script on a loop,
  no server started.
- The ingestor's tuning knobs on prod (`ManualSeed__BatchSize`,
  `MatchIngestion__BatchSize`…) are documented in `docs/prod.md`.

### CPU caps and probe cadence on preprod

The preprod host is a 2 vCPU VPS shared with other stacks, and its provider throttles the whole
machine to 20 % of its CPU when it stays saturated for long enough — which it did on 2026-09-18,
for hours, with a load average of 28 and 90 % steal. Nothing in the stack had a CPU limit, so any
one container could take the machine down with it.

- Every service in `compose.preprod.yaml` carries `cpus` (a hard ceiling) and `cpu_shares` (its
  weight when everything wants CPU at once). The ceilings deliberately add up to more than 2: they
  cap a runaway, while the shares decide who yields under contention. The ordering is what matters,
  not the exact numbers, which the compose file holds: Postgres alone at the top, because every
  other service waits on it; the rest of the request path below it, with the analytics stack
  (`umami`, `umami-db`, `umami-proxy`) level with it; and last the services nobody waits on — the
  admin, the cleanup sidecar. The analytics stack was last until #2153: during an aggregate pass
  Postgres took nearly all the CPU, Umami's API answered in seconds and the admin's Analytics
  page stayed blank, and the operator does wait on that page. `cpu_shares` has
  no `deploy.resources` equivalent, which is why these are the flat Compose keys and not a
  `deploy:` block.
- `compose.prod.yaml` gets no caps: its host is not shared and sizing one machine's limits from
  another's symptoms would be guesswork.
- Health probes ran every 10 s on eleven containers. The probe itself is cheap; the `runc exec`
  Docker spawns to run it is not, and on 2 vCPU the exec machinery was measurably ahead of the
  services it was checking. Postgres and pgbouncer now probe every 30 s, Mongo and the Umami
  database every 60 s — `mongosh` is the expensive one, it boots a full Node runtime per probe.
  `start_period` absorbs the slower startup detection, so `depends_on: service_healthy` still
  gates correctly; boot just takes up to a probe interval longer to be noticed.
- `web/Dockerfile` and `admin/Dockerfile` probe every 30 s for the same reason. Their interval
  lives in the image, so it applies to every stack at once and cannot be re-tuned per environment
  without overriding the whole `healthcheck` block.
- Builds do not run on the preprod host. Images are built in CI and pulled; a `nuxt build` there
  saturates both cores for minutes and is what tipped the machine into the throttle.
