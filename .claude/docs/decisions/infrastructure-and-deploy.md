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
or rollback). The `migrate` job of `rollout.yml` (called by `deploy-preprod.yml`/`deploy-prod.yml`) generates an
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

**The preflight rejects `INGESTOR_JOB_MODE=Full` next to an aggregate lane; the override itself stays.**
Release `1.20.5` gave prod its two ingestor lanes and went green, while a leftover `INGESTOR_JOB_MODE=full`
in the env file (from a key freeze) kept `ingestor` on the whole pipeline: every aggregate step ran twice, once
per container, for about twenty hours. When the compose file declares `ingestor-aggregate`, the `preflight` of
both deploys now fails on `Full` in any casing (`.github/scripts/check-job-mode.sh`). The variable stays the
lever `docs/riot-key-switch.md` uses, so unset and the single-process freeze modes pass. A general post-deploy
read-back of each container's effective `Job__Mode` was considered and left out of scope — #1493 (2026-10-06).

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
job is allowed to conclude: **it polls until the containers actually run the deployed `IMAGE_TAG` and report
healthy, and fails otherwise** (`.github/scripts/verify-rollout.sh`).
↳ **The evidence comes from the Hostinger API, not from SSH, because the CI key is not allowed to answer the
question.** The obvious move was to reuse the channel the migration already has. It cannot work: that key is
pinned to a forced command in the host's `authorized_keys`, so `docker ps` was silently discarded and the
migration script ran in its place, returning nothing. The first version of this check shipped that way and
failed a preprod deploy that had in fact rolled correctly — a false failure found the same hour, which is
the cheap direction for this check to be wrong in. The forced command is right and stays; the deploy job
reads `GET /vps/v1/virtual-machines/{id}/docker` instead, which needs no new secret and reports the live
container list rather than what the compose file intends.
Three choices inside it. The expected images are **read from the compose file**, so a new service is covered
without editing the check and a compose naming none of our images is an error rather than a vacuous pass.
The grain is the **image reference, not the service name** — both environments run two ingestor lanes from
one image (#1374, #1490), and rolling one of them is a partial deploy a per-service check would bless. And **health counts as
much as the tag**, since a container that starts on the right image and crash-loops has not deployed either;
a container with no healthcheck reports empty health and is not read as unhealthy, while `starting` is
polled through, which is what lets a slow roll pass. An API error is fatal rather than an empty container
list, for the same reason the whole entry exists: absence of evidence must not read as evidence.
Verifying was preferred to moving the rollout onto SSH and dropping Docker Manager: that would remove this
failure mode, lose the hosting panel's view of the stack, and still need this check to notice the next
one — #1394, `docs/ci.md`.

**The deploy pre-pulls the images over the CI key before Docker Manager recreates anything (2026-10-05).**
Docker Manager stops and removes every container before it pulls, so a registry error mid-deploy left preprod
fully down for twenty minutes behind a green run. The deploy job now asks the host for `pull <tag>` first and
fails while the old containers still serve. It goes through **the existing CI key**, not a new one: its forced
command became a dispatcher (`.github/scripts/vps/ci-ssh-command.sh`) that accepts only the migration and
`pull <tag>`, the tag matched whole against a git sha or semver regex, and refuses everything else — still no
shell, still no `docker ps`, so the rollout check above keeps reading the Hostinger API. The script is
versioned in the repo but installed on each host by the owner, and the step stays behind a per-environment
`*_PREPULL_IMAGES` variable until it is; the client requires the dispatcher's success line, so a host still on
the old forced command fails the step instead of passing for a pull. Docker Manager's own pull still runs
(`pull_policy: always`), so this narrows that window to a manifest check rather than closing it — #1089,
`docs/ci.md`.

**Both deploy pipelines serialise at workflow level, not per job.**
Preprod needs it because the `-rc.N` counter is read from the remote tags; prod needs it because two
releases published back to back would interleave their `publish` jobs and race for the moving `:latest`
tag. `cancel-in-progress: false` in both: a running deploy finishes, and GitHub collapses the pending
queue to the newest run — a visibly cancelled run, never a half-deploy — #1228.
Nothing else joins `preprod-pipeline`. The preprod load test once did, and a test dispatched while a deploy was
pending cancelled that deploy; it now has its own group, refuses to start while a deploy is queued or running,
and the deploy's preflight waits for a running test — #1566 (2026-09-15).

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
screen inside the iframe whenever no session is active. The replay/heatmap deep links #1014 added beside it
were removed with the recorder in #2089. Revisit if the login-in-iframe friction becomes the bigger
annoyance — #1013.

**Umami session replay/heatmap rows are purged after 7 days by a sidecar container, not left to grow.**
Self-hosted Umami has no built-in retention for `session_replay`/`heatmap_event` — the `retentionDays` label
in its client bundle only surfaces on Cloud-subscription screens. Both are the heaviest tables Umami writes
(a replay is a full stream of DOM mutations), and the sample rate was raised to 100% (owner's call — current
traffic is low enough to afford it). Left unbounded, this repeats the disk-fill shape of #680 (Postgres hit
68 GB, VPS ran out of disk). `umami-replay-cleanup` (all three compose files) is a `postgres:17.2-alpine`
container with its entrypoint overridden to just loop a `psql` purge once a day — no server started, matching
`umami-db`'s Postgres major version. Retention is `UMAMI_REPLAY_RETENTION_DAYS`, default 7 — #1018.
Since #2089 the site records no replay or heatmap; the sidecar stays so the rows recorded before are purged on
the same retention, and so a re-enabled recorder can never fill the disk again. Since #2092 the same daily pass
also enforces the CNIL retention and minimisation rules on the rest of Umami's data (next entry); the service
keeps its name, because Docker Manager leaves a renamed container running as an orphan.

**Analytics stay inside the CNIL consent exemption for audience measurement — no consent banner (2026-10-18).**
The owner wanted to see recurring visitors, possibly by name. Checked against the CNIL's self-assessment grid
for consent-exempt audience measurement (July 2025): a first-party identifier is allowed (13 months max, not
extended per visit), but "no tracking of a single user's navigation" is a criterion, its prescribed measure is
disabling session replay, reports must stay anonymous whatever the filters, and an opt-out link in the privacy
policy is required. The CNIL's February 2026 draft recommendation on session replay puts replay under prior
consent. The owner chose the exempt path over a consent banner: Umami's `recorder.js` is no longer loaded, no
`umami.identify()` or stored visitor id is added (Umami's own visitor hash — site + IP + user agent + monthly
salt — is the fingerprint shape the grid accepts), and `/privacy` carries an opt-out switch on Umami's native
`umami.disabled` flag. Recurrence is read in aggregate (Umami's Retention report), within a calendar month.
Seeing individual recurring visitors would need a consent banner — reopen only on that basis. Known residual
gaps against the grid: Umami keeps the full referrer and UTM parameters, and its Sessions view lists visitors
one by one — #2089.
**Partly reversed on 2026-10-19 (#2149): a persistent anonymous visitor id now exists.** The owner wanted to
know whether visitors come back *across* months, which the monthly salt makes impossible. The grid allows a
first-party identifier, so `utils/analytics-visitor-id.ts` stores a random UUID (tied to nothing — not the IP,
not a Riot account) in `localStorage` with its creation date, reused for 13 months from creation, never
extended by a later visit, replaced once expired, removed on opt-out. Umami 3.2.0's collector derives the
session from a payload `id` when one is sent (`uuid(websiteId, id)`, no IP, user agent or salt), so a returning
visitor stays one session for 13 months; storage that cannot keep the id sends none (Umami's hash), never a
per-page-load id. The id rides on every send through the tracker's `before-send` hook, not
`umami.identify()`: identify runs after the automatic first page view, which then lands in a second,
IP-hashed session on every page load. Still no `identify()`, no replay, no named visitors; `/privacy`
describes the identifier. The Sessions-view gap above now spans 13 months instead of one.
**Referrer, UTM and retention gaps closed on 2026-10-24 (#2092).** Three measures of the grid we did not apply:
- *Campaign ids are not collected.* Umami's collector parses `utm_*` and six ad click ids out of the page URL
  into columns, and keeps the whole query string. The tracker's `data-exclude-search` would drop them, but
  also every page filter (`patch`, `elo`, `position`, `vs`…) content analysis reads, so it is not used:
  `utils/analytics-payload.ts` strips `utm_*` and click ids from the URL in the same `before-send` hook that
  stamps the visitor id, and leaves the rest of the query as it was.
- *The referrer is limited to its domain.* An external referrer leaves the browser as its origin alone (stored
  as the domain with path `/`). A same-site referrer is dropped: Umami 3.4's tracker sends the previous page as
  a bare path on reloads and client-side navigations, and its collector stores that path and query even though
  it blanks the referrer domain — a per-visitor page chain.
- *Collected data is kept 25 months at most.* The `umami-replay-cleanup` sidecar deletes `website_event`,
  `event_data`, `revenue`, `session_data` and `session_link` rows older than `UMAMI_DATA_RETENTION_MONTHS`
  (default 25), then the sessions older than that with no event left (Umami declares no foreign keys, so
  every child table is purged by itself).
The tracker side is not enough on its own: the collect endpoint is public and pages cached before the change
still run the old bundle. The same daily pass therefore rewrites anything that slipped through to the same
shape — referrer path `/` and empty query, same-site referrers blanked, campaign columns nulled and the
parameters cut from `url_query` — which also scrubs the rows recorded before #2092 on its first run. Keep the parameter list
in `analytics-payload.ts` and the sidecar's regex in step. Remaining known gap: Umami's Sessions view.

**`/ops/*` is the only authenticated API surface** (`X-Ops-Key`, min 32 chars, rotated independently of the
Riot key). Everything else is public and rate-limited to 500 req/min per *visitor* — `docs/api.md`.

**The rate-limit partition is the visitor, and the trust in `X-Forwarded-For` is bounded twice.**
Until #1546 the limiter keyed on the connection address, which sounds per-client and is not: no browser
reaches the API directly, so every public read arrives from the web container and every ops call from the
admin one. The documented "100 req/min per IP" was therefore a site-wide ceiling of ~1.6 req/s shared by
everyone at once, and the first burst of real traffic (a Reddit post) spent it in seconds — the site served
its own visitors 429s, then 502s and 504s as the queue backed up. The key is now the *last* entry of
`X-Forwarded-For`, and only when the connection comes from a range listed in `RateLimit:TrustedProxies`
(the private networks by default). Both halves are load-bearing: earlier header entries are written by the
caller, and the API container publishes port 8080 on the host, so "only Caddy can reach it" is a firewall
property rather than an application one. The three numeric knobs bind from `RateLimit:*` and carry an env
override in both compose files, because a traffic spike is precisely when a compiled-in constant is the
slowest lever available — prod only rolls on a published release. `TrustedProxies` is a list and stays a
code default: which networks may speak for a visitor is a deployment shape, not an incident lever — #1546.

**The Riot API key is a permanent *personal* key — not a 24 h dev key, and not production-approved.**
(Owner-confirmed terminology, 2026-07-28: do not call it a dev key.) The production application is submitted
and pending review. It declares ACCOUNT-V1, SUMMONER-V4, MATCH-V5, LEAGUE-V4 and CHAMPION-MASTERY-V4 —
**not SPECTATOR-V5**. Consequences: no live-game features (#532, parked P3), no RSO and therefore no user
accounts (#780), ingestion is rate-limited, and data changes are forward-only because backfill is not
possible. Approval is the single external unlock for all of it — #780.

## Preprod runs prod's parameters at a smaller volume, behind its own edge Caddy (2026-09-14)

**Decision:** `compose.preprod.yaml` may differ from `compose.prod.yaml` only in **volume** (ingestion batch
sizes, retention, Postgres memory sizes kept at prod's ratio to RAM), **identity** (Riot key, names, ports,
origins, secrets, TLS) and **documented trials** (`AggregateNonMainPopulation`). Everything else matches, and
a plain-HTTP `caddy` service fronts web and admin on preprod's ports — #1558.

Preparing a load test (#1560) forced the inventory, and preprod had drifted in ways that would have made its
numbers meaningless. With nothing in front of the apps, the API never saw an `X-Forwarded-For` and put every
preprod visitor in one rate-limit bucket, and the admin throttle could not trust the header. Main detection ran
on 10/30/10 matches instead of 20/50/20 — a different definition of a main, not a smaller sample of the same one.

- **Memory settings scale; they are not copied.** `shared_buffers=4GB` on a 7.7 GB shared host invites the OOM
  this project has already had (#601). Prod's *ratios* carry over, and the preprod values already met them.
- **The Caddyfile is inline** under `configs:`, because the deploy hands Docker Manager the compose file only —
  a sibling `Caddyfile` would have to be placed on the host by hand.
- **Plain HTTP stays.** Preprod has no DNS name, and the host's 80/443 belong to another project; the admin
  cookie stays non-`Secure`, which is an identity difference, not drift.
- **`AggregateNonMainPopulation` stays a preprod-only trial**, kept by the owner's choice rather than aligned.

## Preprod runs at test volume on its shared host, not at load-test size (2026-09-16)

**Decision:** preprod goes back to 10/30/10 main detection and one web worker; it verifies changes before a
release and is no longer the target for capacity tests. This reverses the main-detection part of the entry above.

- **Why.** On 2026-09-15 a day of 200-visitor load tests, prod's 20/50/20 ingestion and a two-worker web server
  on the shared 2-vCPU VPS starved everything on it (about 90 % CPU steal, TLS handshakes of several seconds for
  the other projects), and the owner stopped the preprod stack by hand.
- **Consequence.** Main detection on preprod works from a smaller sample than prod; the edge Caddy, compression,
  pools and every non-volume parameter still match prod. The load-test workflow stays for `smoke` checks; a
  capacity run needs a host of its own.
- **The app containers run an init** (`init: true`): a health check every 10s leaves its finished process
  reparented to PID 1, and Node/.NET do not reap it — 103 zombies in prod's web container in four days. Docker's
  `tini` clears them.
- **Health checks are patient** (20s timeouts, 90s start periods, doubled retries, same checks). When the stack
  starts, the provider's CPU limitation kicks in and a `mongosh` ping measured 52s on the host; with 5s timeouts
  Docker Manager saw a healthy Mongo as an unhealthy dependency and abandoned two deploys in a row.

**The edge sets the response security headers the app frameworks did not — 2026-09-24.** The public site and admin shipped no `Strict-Transport-Security`, `Content-Security-Policy`, `X-Frame-Options`, `X-Content-Type-Options`, `Referrer-Policy` or `Permissions-Policy` (the API's `UseHsts` never reached the browser, which talks to Nuxt through Caddy, not the API). A shared `(security_headers)` snippet in the `Caddyfile` now carries all of them and strips the upstream `X-Powered-By`, imported into the public and admin vhosts only — not `analytics`, where Umami's own CSP lives and an `X-Frame-Options` would break the admin iframe. Each app vhost adds its own CSP: the public site allows the Umami tracker host under `script-src`/`connect-src`, the admin allows the Umami dashboard under `frame-src`; both keep `'unsafe-inline'` (Nuxt hydration, chart inline styles, no nonce pipeline) and the two game-asset CDNs under `img-src`. Preprod's inline edge carries the snippet minus HSTS (plain HTTP) and minus the CSP (its analytics host is an operator env value, not a fixed origin). See `docs/prod.md`.

**The API port is published on loopback only, and pgAdmin is gone — 2026-09-24.** Both deployed stacks bound the API on `0.0.0.0` (`8080:8080` prod, `8081:8080` preprod), so it sat on the VPS public interface behind the host firewall alone — the one thing `ClientAddressResolver` already warns can fail invisibly. Nothing external needs the port: web and admin reach the API as `http://api:8080` over the internal network and Caddy proxies to web/admin, never to the API. It is now `127.0.0.1:8080:8080` / `:8081:` — host-local diagnostics keep working, the public interface does not, so "Caddy is the only public entry" is finally true at the socket level. pgAdmin was removed from every stack in the same pass: on preprod it published a password-only DB console on `0.0.0.0:5051`, a standing internet-reachable surface with no edge in front, redundant with `psql` on the loopback Postgres port.

**Prod's edge configuration is inline in `compose.prod.yaml`, and the release checks the live edge — 2026-10-05.** The prod `Caddyfile` was a tracked file bind-mounted from the deployment directory, which no deploy writes: Docker Manager receives the compose file and the `.env`, nothing else. Compression (#1583) and the security headers (#1696) were released green and absent from prod — the host ran a hand-copied file. The product owner chose to embed the configuration under `configs.edge_caddyfile`, as preprod already did, over a rollout step copying the file: the deploy then cannot ship one without the other, and no new channel to the host is needed (the CI SSH key is pinned to the migration command). The rule now covers every deployed stack: whatever configuration it needs lives inside its compose file, and a relative bind mount is a file someone must copy by hand. An `edge` job in `deploy-prod.yml` asserts after each rollout that the public site answers HTML and JSON with `Content-Encoding` zstd/gzip and sends `Strict-Transport-Security` and `Content-Security-Policy`. See `docs/ci.md` (*What the deploy writes on the VPS*, *Verifying the edge*) — #1598.

## Each service gets a fixed share of PgBouncer's server connections (2026-10-19)

**The API's PgBouncer database is capped at 22 server connections and the ingestors' at 8, with no reserve pool;
every Npgsql `Maximum Pool Size` is sized to its share (API 22, fetch lane 5, aggregate lane 3), so the client
pools add up to the 30 server connections.** Before, the client pools summed to 110 (30 + 40 + 40) against two
PgBouncer pools of 25 + 5 under one `max_user_connections = 30`: whichever side was busy could hold all 30 and
leave the other queued at PgBouncer for up to 30 s. Reproduced locally on the deployed config: 35 long ingestor
transactions took all 30 server connections and an API query waited 7.5 s; on the new config the ingestors stop
at 8 and the API query answers at once.

- *A per-database cap, not a new role.* The API already had its own PgBouncer database (`${POSTGRES_DB}_api`,
  #1631), so `pool_size` + `max_db_connections` on each entry isolate the two without a second Postgres user —
  no new secret in either host's `.env`.
- *The split follows the code.* Each ingestor lane runs its steps sequentially; the only parallel database work
  is the fetch lane's match ingestion, one worker per active platform (three). The lanes keep headroom above
  that, the rest goes to the API, whose reads fan out (up to six queries per leaderboard request).
- *The total stays 30*, what PgBouncer served before, far under Postgres `max_connections=100`; it is not grown
  without a measurement.
- *Not yet observed in preprod.* The issue's acceptance — `cl_waiting` at 0 for the API pool with both lanes at
  full load, under the normal pipeline — stays open on #1632 until checked.

Source: #1632; `docs/prod.md` (*Connection pools*). Supersedes the "ingestors keep their own caps (40 each)" and
"capacity is unchanged" notes in `performance-and-incidents.md` (#1570, #1631).

## The edge is its own Docker Manager project and serves a maintenance page while the stack rolls (2026-10-20)

**Caddy runs in a project of its own (`truemain-edge` from `compose.prod.edge.yaml`, `truemain-preprod-edge` from
`compose.preprod.edge.yaml`), and when it cannot reach web or admin it answers a `503` maintenance page (JSON on
`/api/*`) instead of a `502`.** Docker Manager recreates every container of a project on each deploy — measured on
the 1.25.1 release log, Caddy included: the public site answered nothing for ~40 s on prod (~57 s on preprod), then a
bare `502` until web listened. A maintenance page served by the in-stack Caddy would only have covered the part of
the window where Caddy happened to be up, so the product owner chose to move the edge out of the stack.

- *The app stack owns the edge network* (`truemain_edge` / `truemain_preprod_edge`); the edge joins it as
  `external`. The app never depends on the edge being deployed, and the edge sees web, admin and Umami only.
- *The edge redeploys only when it changes* — missing project, a container not running, or a compose file that
  differs from the repository's (`edge-drift.sh`) — because a redeploy recreates Caddy.
- *`503` + `Retry-After`, `no-store`*: crawlers read a temporary outage, and no cache keeps the page afterwards.
  An app-level `503` passes through; only a failure to reach the upstream is maintenance.
- *No manual maintenance switch*: the page appears exactly when the upstream is unreachable. A toggle for planned
  work with the apps still up was not asked for.
- *Not on `analytics`*: an HTML body in place of the Umami tracker helps nobody.

Source: #2179; `docs/prod.md` (*Maintenance page*, *One-time cutover to the edge project*), `docs/ci.md` (*The edge
is its own project*). Moves the edge configuration from `compose.prod.yaml` / `compose.preprod.yaml` (2026-10-05
entry above, #1598; 2026-09-14 entry, #1558) to the edge files; the inline-config rule itself is unchanged.
