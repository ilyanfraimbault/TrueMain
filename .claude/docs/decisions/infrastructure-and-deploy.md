# Infrastructure and deploy

Part of the [decision log](../decisions.md). Format: **Decision** — why — `source`.

**The admin portal is a standalone Nuxt app with its own deployment and domain — not a `/admin` route.**
Decided 2026-06-09. Auth is username/password + a signed httpOnly session; the app injects `X-Ops-Key`
server-side so the ops key never reaches the browser. Native composables, no Pinia — #96, #91, #376.

**Preprod auto-deploys from `compose.preprod.yaml` on every push to `develop`; prod auto-deploys from `compose.prod.yaml` only when a GitHub Release is published.**
Merging to develop or master alone never reaches prod. Both use the `hostinger/deploy-on-vps` action (a pure
API call, no SSH material in CI). Prod and preprod are on **separate Hostinger accounts**, so each needs its
own account-scoped token — `docs/prod.md`, `docs/preprod.md`, #717, #751.

**Images publish an immutable `:<sha>`/`:<version>` tag alongside the moving `:preprod`/`:latest`, and compose references the immutable one.**
The classic mutable-tag trap: the deploy job went green while the VPS kept running days-old containers,
because handing Docker Manager an unchanged compose spec never triggers a pull or recreate. Changing the
referenced image name every merge forces the pull. Doing so then surfaced a hidden GHCR `unauthorized`
failure the mutable tag had been masking — #738, #765, #767.

**Prod deploys from the version-controlled compose file — no hand-maintained host compose.**
A divergent host-only `docker-compose.yml` meant the pool-cap fix never reached prod, the uncapped pools kept
running and the `53300` outage returned — #462.

**Preprod and prod both apply migrations out-of-band, as a discrete CI step before the images roll — not at startup.**
`Database__ApplyMigrationsOnStartup` is `false` in both `compose.preprod.yaml` and `compose.prod.yaml`
(Microsoft advises against startup migration under concurrency: elevated app-account privileges, no review
or rollback). The `migrate-preprod`/`migrate-prod` jobs in `deploy-preprod.yml`/`deploy-prod.yml` generate an
idempotent SQL script from the deployed commit/tag and apply it over SSH by piping it into `psql` inside the
running Postgres container — neither VPS exposes a connection reachable from a GitHub-hosted runner. The
deploy job depends on the migrate job, so a failed migration blocks the image roll. Preprod runs this on
every merge to `develop`, so it catches a bad migration before it ever reaches prod. The credential is still
the app's own `POSTGRES_USER`, not a dedicated migration-only role — splitting one off is open follow-up
work, not this decision — #208, #246, #1058, `docs/production-migrations.md`.

**An incomplete prod deployment configuration fails the release run; it is never a green skip.**
Because migrations apply *before* the images roll, checking the deploy-side configuration at deploy time
could only ever produce the mismatch in the other direction: an empty `PROD_ENV_FILE` or an unset
`HOSTINGER_PROD_VM_ID` skipped the image roll — green — after `migrate-prod` had already moved the schema,
leaving prod on the old binary against the new one. All of it (SSH secrets, `PROD_ENV_FILE`,
`HOSTINGER_PROD_API_KEY`, `HOSTINGER_PROD_VM_ID`) is now checked by a `preflight` job that every other job
depends on, `publish` included — publishing would otherwise move `:latest` ahead of what prod runs. Both
halves of a release happen or neither does — #1228.

**A deploy job proves the environment moved; the API acknowledgement is not evidence.**
#1228 made an incomplete *configuration* fail the run rather than skip green. Release `1.20.0` produced the
same mismatch through the door that check does not watch: everything was configured, all seven jobs were
green, the migrations applied, and prod went on running the previous release's images for forty minutes —
the new schema against the old binaries, with #1365's re-fold migration meanwhile deleting the live-patch
aggregates and re-arming their flags so the *old* ingestor rebuilt them under the old cohort rule.
`hostinger/deploy-on-vps` never uploads the compose file: it hands Docker Manager a URL and Docker Manager
**clones this repository from the VPS**, so the action reports success on a 2xx from the API, before the only
step that can fail. That clone failed (the host had no registered key and GitHub answers `401` to
unauthenticated git), and nothing in the pipeline was watching.
The rule this settles is not about that clone, which is a repairable host problem, but about what a deploy
job is allowed to conclude: **it polls the host until the containers actually run the deployed `IMAGE_TAG`
and report healthy, and fails otherwise** (`.github/scripts/verify-rollout.sh`, over the same SSH channel the
migration already uses). Three choices inside it. The expected images are **read from the compose file**, so
a new service is covered without editing the check and a compose naming none of our images is an error
rather than a vacuous pass. The grain is the **image reference, not the service name** — preprod runs two
ingestor lanes from one image (#1374), and rolling one of them is a partial deploy a per-service check would
bless. And **health counts as much as the tag**, since a container that starts on the right image and
crash-loops has not deployed either; `health: starting` is polled through rather than failed, which is what
lets a slow roll pass. Verifying was preferred to moving the rollout onto SSH and dropping Docker Manager:
that would remove this failure mode and lose the hosting panel's view of the stack, and it would still need
this check to notice the next one — #1394, `docs/ci.md`.

**Both deploy pipelines serialise at workflow level, not per job.**
Preprod needs it because the `-rc.N` counter is read from the remote tags; prod needs it because two
releases published back to back would interleave their `publish` jobs and race for the moving `:latest`
tag. `cancel-in-progress: false` in both: a running deploy finishes, and GitHub collapses the pending
queue to the newest run — a visibly cancelled run, never a half-deploy — #1228.

**Integration tests run on pushes to `develop`/`master`, not only on pull requests.**
The push to `develop` is the commit that deploys to preprod and the develop→master merge is the one a
release is cut from — the two trees whose behaviour is about to hit a real environment were the only ones
skipping the Testcontainers suite, and a squash merge produces a tree no PR run ever tested. The cost is a
few Testcontainers minutes per merge — #1228.

**Preprod tracks `develop`, has its own Riot API key, and is deliberately tiny — a new key forces an empty database.**
PUUIDs are encrypted per API app, so key and database are an inseparable pair: old data is unusable with a new
key. Preprod runs every pipeline stage at reduced volume with 1-patch retention — `docs/preprod.md`, #705.

## Preprod builds carry a prerelease version, tagged only after they deploy (2026-08-24)

Every push to develop reaches preprod a few minutes later, but nothing on the page said *which* build was
serving it — "is my change on preprod yet?" and "did this reach prod?" were answerable only by comparing SHAs
on GitHub. `deploy-preprod.yml` now resolves `<base>-rc.<N>` and the footer prints it (`preprod · 1.20.0-rc.4`);
prod prints the bare release tag it is serving.

Three calls worth keeping:

- **A prerelease, not build metadata.** `1.19.0+7` (commits since the last release, derivable with a single
  `git describe` and no tags at all) was the cheaper design and was rejected: the same string tags the four
  images on GHCR, and `+` is **illegal in a Docker reference**. `-rc.N` is legal, so the version can be one
  string everywhere — footer, git tag, image tag.
- **The tag is pushed after the deploy succeeds, not at build time.** The tag's whole job is to mean "this ran
  on preprod". Tagging in the publish job would mint tags for commits that never got there, recreating the
  ambiguity it replaces. Cost: a build that fails after publishing leaves a gap in the sequence, which is the
  honest reading.
- **The base is a label, not a promise.** It defaults to the next *minor* after the latest release, because
  that is what a plain "release" cuts — but the real bump is still decided by the user's word at release time
  (see the `release` skill), so a `1.20.0-rc.*` line can perfectly well ship as `1.19.1`. Set the
  `PREPROD_VERSION_BASE` repo variable when the next one is known to be a major.

The trap this introduces, and it bit during implementation: **git's version sort ranks `1.20.0-rc.4` above
`1.20.0`**. Anything reading "the latest version" must filter to bare `MAJOR.MINOR.PATCH` or it will read a
preprod build as the last release and skip a version on the next bump. The `release` skill was updated for
exactly this.

**Caddy terminates TLS and is the only public entry point in prod.**
Admin login was impossible over cleartext HTTP because the session cookie is sealed `Secure` in production
builds. Caddy also normalises `X-Forwarded-For` (dropping client-supplied values), which is what makes the
admin brute-force throttle non-spoofable. DNS must be **DNS-only, not Cloudflare-proxied**, or the ACME
challenge fails — #433, #430, #426.

**The admin `/analytics` iframe stays on Umami's public share view, not the authenticated app — kept as-is on purpose, 2026-08-04.**
The authenticated app *can* be framed: Caddy rewrites `analytics.truemain.lol`'s `frame-ancestors` to allow
`admin.truemain.lol` (`Caddyfile`), and the two subdomains share `truemain.lol` so the Umami session cookie
would reach the iframe. That rules out CSP as the reason to prefer the share view. The owner chose to keep it
anyway: the share view renders with no Umami login, while the authenticated app would show Umami's login
screen inside the iframe whenever no session is active. Session replays/heatmaps (#1013) — absent from the
share view's hardcoded nav because a share link is an unauthenticated public URL and a replay is a full DOM
recording — stay reachable only via the deep links added in #1014, opened in a new tab. Revisit if the
login-in-iframe friction becomes the bigger annoyance — #1013.

**Umami session replay/heatmap rows are purged after 7 days by a sidecar container, not left to grow.**
Self-hosted Umami has no built-in retention for `session_replay`/`heatmap_event` — the `retentionDays` label
in its client bundle only surfaces on Cloud-subscription screens. Both are the heaviest tables Umami writes
(a replay is a full stream of DOM mutations), and the sample rate was raised to 100% (owner's call — current
traffic is low enough to afford it). Left unbounded, this repeats the disk-fill shape of #680 (Postgres hit
68 GB, VPS ran out of disk). `umami-replay-cleanup` (all three compose files) is a `postgres:17.2-alpine`
container with its entrypoint overridden to just loop a `psql` purge once a day — no server started, matching
`umami-db`'s Postgres major version. Retention is `UMAMI_REPLAY_RETENTION_DAYS`, default 7 — #1018.

**`/ops/*` is the only authenticated API surface** (`X-Ops-Key`, min 32 chars, rotated independently of the
Riot key). Everything else is public and rate-limited to 100 req/min per IP — `docs/api.md`.

**The Riot API key is a permanent *personal* key — not a 24 h dev key, and not production-approved.**
(Owner-confirmed terminology, 2026-07-28: do not call it a dev key.) The production application is submitted
and pending review. It declares ACCOUNT-V1, SUMMONER-V4, MATCH-V5, LEAGUE-V4 and CHAMPION-MASTERY-V4 —
**not SPECTATOR-V5**. Consequences: no live-game features (#532, parked P3), no RSO and therefore no user
accounts (#780), ingestion is rate-limited, and data changes are forward-only because backfill is not
possible. Approval is the single external unlock for all of it — #780.
