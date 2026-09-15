# Load testing

How TrueMain is load-tested and how to read the result. The test runs against **preprod**, from a
**GitHub-hosted runner**, with [k6](https://grafana.com/docs/k6/) (`loadtest/k6/`), and is started by hand from
the **Load test preprod** workflow (#1559).

## Why this shape

- **Preprod, not prod.** A test that finds the ceiling finds it for real visitors too. Preprod runs prod's
  parameters at a smaller volume (`docs/preprod.md`, *Parity with prod*), so its numbers are comparable in kind,
  not in size: its host has half prod's cores and RAM, and shares them. Read a preprod result as a lower bound,
  and never scale it to prod with a multiplier.
- **From GitHub Actions, never from the preprod host.** A generator on the host takes CPU from the system under
  test, and the result measures both.
- **k6 does not run the site's JavaScript.** Each page view is replayed the way a browser performs it: the
  server-rendered HTML, then the `/api/*` calls the page makes once hydrated, the static lookups once per session
  (the browser keeps them for an hour) and, with `fetch_assets`, the page's bundles and images once per session.
  Umami never records these visits.
- **The slices are spread.** Champion reads are cached per champion × position × rank bracket × patch, so each
  visitor draws them at random; a test that repeated one slice would time the cache instead of Postgres.

## Running it

Actions → *Load test preprod* → *Run workflow*:

| Input | Default | Meaning |
| --- | --- | --- |
| `scenario` | `smoke` | `smoke`: 1 visitor for 1 minute. `visitors`: the capacity test. `ratelimit-probe`: one client over the rate limit for 1 minute. |
| `vus` | 200 | concurrent visitors (`visitors`) |
| `ramp` / `hold` | `3m` / `10m` | ramp-up and time at full load (`visitors`); ramp-down is 2 minutes |
| `fetch_assets` | `true` | also fetch bundles and images; turn it off to isolate SSR and the API |

The workflow shares the preprod deploy's concurrency group: a deploy waits for a running test instead of
redeploying under it. Run `smoke` first after any change to the script or to the pages it replays.

Against any origin, locally: `k6 run -e BASE_URL=http://localhost:3001 -e SCENARIO=smoke loadtest/k6/run.js`.

## What a visitor does

A session is 3 to 6 page views, with 5 to 20 seconds of reading between them, drawn from:

| Journey | Share | Requests after the HTML |
| --- | ---: | --- |
| champion page | 40 % | champion, trend, scaling, roam, power spikes, item context, matchups, synergies, trios |
| home | 20 % | overview, leaderboard teaser |
| tier list | 15 % | tier list for a random lane and bracket |
| champions list | 10 % | directory for a random bracket |
| truemains | 10 % | none — the leaderboard renders server-side |
| player profile | 5 % | profile, rank history, activity, matches |

The request lists mirror the page composables in `web/app`. When a page starts or stops fetching something,
`loadtest/k6/lib/journeys.js` has to follow, or the test drifts away from what visitors actually cause.

## The rate limit and a single runner

The API allows each visitor `RateLimit:PermitLimit` requests per window (500 a minute by default). Every virtual
visitor of a run shares the runner's address, so a capacity run measures the limiter within seconds unless the
limit is raised for its duration. That is an operator step on the host, confirmed every time and reverted
afterwards: set `RATE_LIMIT_PERMITS` in the preprod `.env`, recreate the `api` container, run, restore it. A
deploy restores it too, since the `.env` is rewritten from the `PREPROD_ENV_FILE` secret.

`ratelimit-probe` does the opposite: it keeps the limit and checks that the rejections reach the admin Logs as
`RateLimitRejected` rows keyed on the runner's address. That is the end-to-end proof that the edge proxy, the SSR
header forwarding and the logging all work, and it belongs before any long run.

## Reading the result

The job page carries the summary and the artifact holds the same numbers as JSON. Neither contains a URL or the
host: requests are named by route template, and the workflow refuses to publish any output that names the host.

- **Thresholds** — failed page and API requests under 1 %, page p95 under 1.5 s, API p95 under 2 s, checks over
  99 %. They report and never stop the run; a crossed threshold fails the job once the summary is published.
- **Responses** — a 404 is a champion without data for the slice, which is an answer; a 429 is the limiter; a
  5xx is the site failing; *no answer* is a timeout or a dropped connection.
- **Per route** — where the latency and the failures are.

Every error class has a counterpart in the admin Logs page: `RequestFailed` (an API 5xx), `RateLimitRejected`,
`RequestAborted` (a client gave up), `FrontendServerError` and `FrontendUpstreamErrors` (the web tier) and
`LogRecordsDropped` (the log channel itself overflowed) — #1555, #1556. A class k6 counted with no matching rows is
a gap in the logging, not in the site.

Host-side numbers — CPU and memory per container, pgbouncer pool wait, slow queries, restarts — are not in the
summary: they are sampled on the host while the test runs.
