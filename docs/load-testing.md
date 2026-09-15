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
- **Load over HTTP, page loads in a browser.** k6's HTTP visitors do not run the site's JavaScript. Each page
  view is replayed the way a browser performs it: the server-rendered HTML, then the `/api/*` calls the page
  makes once hydrated, the static lookups once per session (the browser keeps them for an hour) and, with
  `fetch_assets`, the page's bundles and images once per session. That is cheap enough for hundreds of
  visitors, but it cannot say when a page is loaded: most of the champion page, including its hundreds of
  icons, exists only once its JavaScript has fetched it. So a few real browsers (`k6/browser`, Chromium) load
  the same pages while the load runs, and time them (#1572). Umami never records either kind of visit.
- **The slices are spread.** Champion reads are cached per champion × position × rank bracket × patch, so each
  visitor draws them at random; a test that repeated one slice would time the cache instead of Postgres.

## Running it

Actions → *Load test preprod* → *Run workflow*:

| Input | Default | Meaning |
| --- | --- | --- |
| `scenario` | `smoke` | `smoke`: 1 visitor for 1 minute and one browser pass. `visitors`: the capacity test. `browser`: browsers only, the page-load reference at rest (3 passes). `ratelimit-probe`: one client over the rate limit for 1 minute. |
| `vus` | 200 | concurrent visitors (`visitors`) |
| `ramp` / `hold` | `3m` / `10m` | ramp-up and time at full load (`visitors`); ramp-down is 2 minutes |
| `fetch_assets` | `true` | also fetch bundles and images; turn it off to isolate SSR and the API |
| `browser_vus` | scenario default | browsers timing full page loads: 1 for `smoke` and `browser`, 3 for `visitors` (during the hold only); `0` turns them off |

A test refuses to start while a preprod deploy is queued or running: dispatch it again once the deploy has
finished. A deploy triggered during a test (a merge to `develop`) waits for the test before rolling out, so do
not merge while a long run is on. Run `smoke` first after any change to the script or to the pages it replays.

Against any origin, locally: `k6 run -e BASE_URL=http://localhost:3001 -e SCENARIO=smoke loadtest/k6/run.js`.
The browsers need Chrome or Chromium: point `K6_BROWSER_EXECUTABLE_PATH` at it when it is not on the `PATH`, and
add `K6_BROWSER_ARGS=no-sandbox` inside a container.

## What a visitor does

A session is 3 to 6 page views, with 5 to 20 seconds of reading between them, drawn from:

| Journey | Share | Requests after the HTML |
| --- | ---: | --- |
| champion page | 40 % | champion, trend, scaling, roam, item context, matchups, synergies; then power spikes once per build tab |
| home | 20 % | overview, leaderboard teaser |
| tier list | 15 % | tier list for a random lane and bracket |
| champions list | 10 % | directory for a random bracket |
| truemains | 10 % | none — the leaderboard renders server-side |
| player profile | 5 % | profile, rank history, activity, matches |

The request lists mirror the page composables in `web/app`, including their conditions: power spikes are
requested only once the champion answered, one per build tab since every tab's panel stays mounted (the API
refuses them without the build's key), and duo trios
not at all (the page fires them only after a visitor picks a partner). When a page starts or stops fetching
something, `loadtest/k6/lib/journeys.js` has to follow, or the test either drifts from what visitors cause or
fails on requests no visitor sends. A stub origin cannot catch that: check a change with `smoke` against
preprod.

## What a browser measures

A browser pass loads every page template once, in turn: a champion page (random champion, lane and bracket),
home, the tier list, the champions list, the truemains leaderboard and a player profile. Each load is a first
visit, in a fresh browser context with an empty cache. Requests leaving the site's origin, analytics among them,
are aborted. From navigation start, it records:

| Figure | Meaning |
| --- | --- |
| TTFB, LCP | first byte of the HTML; largest contentful paint, read before any scroll |
| data ready | the last `/api/*` answer of the landing view |
| view loaded | the landing viewport settled: no request to the site in flight for 750 ms, last resource (images included) answered |
| page loaded | the whole page scrolled through, then each image still loading brought into view in turn, until the network is quiet again, so the sections that load once visible and the lazy images count. The scroll is paced, so this figure includes a few seconds of scrolling |
| images, failed / never loaded | images rendered on the page; those that answered without a usable image; those still not loaded after being brought into view (a lazy image clipped inside its container never starts) |
| incomplete | loads that did not reach *page loaded* within 60 s, or whose HTML failed |

*Quiet* means no fetch of the page's own in flight, no image loading in the viewport and no new resource for
750 ms. It is read from inside the page: k6's request events keep a browser iteration from ending.

Images hidden on the page (inactive build tabs) are left out, since a browser never loads them. Every figure
is tagged with the page template, never with a URL.

## The rate limit and a single runner

The API allows each visitor `RateLimit:PermitLimit` requests per window (500 a minute by default). Every virtual
visitor of a run shares the runner's address, so a capacity run measures the limiter within seconds unless the
limit is raised for its duration. That is an operator step on the host, confirmed every time and reverted
afterwards: set `RATE_LIMIT_PERMITS` in the preprod `.env`, recreate the `api` container, run, restore it. A
deploy restores it too, since the `.env` is rewritten from the `PREPROD_ENV_FILE` secret.

`ratelimit-probe` does the opposite: it keeps the limit and checks that the rejections reach the admin Logs as
`RateLimitRejected` rows keyed on the runner's address. It sends 16 requests a second: fifteen of a cached API
read, so the limit is crossed however slow the pages are, and one server-rendered page, whose own call to the
API must be charged to the same visitor. Expect a first-rejection row and a per-window count on process `Api`
(the count exceeds k6's 429s by the rejected SSR calls, whose pages still render), and counted
`FrontendUpstreamErrors` rows on process `Web`. That is the end-to-end proof that the edge proxy, the SSR header
forwarding and the logging all work, and it belongs before any long run.

## Reading the result

The job page carries the summary and the artifact holds the same numbers as JSON. Neither contains a URL or the
host: requests are named by route template, and the workflow refuses to publish any output that names the host.

- **Thresholds** — failed page and API requests under 1 % and checks over 99 % on every run; page p95 under
  1.5 s and API p95 under 2 s on `visitors` only, since a one-visitor smoke run on cold caches is slow by
  construction. With browsers: incomplete loads under 1 % on every run; on `visitors` and `browser`, a champion
  page fully loaded at p95 under 6 s, data ready p95 under 3 s and LCP p95 under 2.5 s. They report and never stop the run; a crossed threshold fails the job once the summary is
  published.
- **Responses** — a 404 is a champion without data for the slice, which is an answer; a 429 is the limiter; a
  5xx is the site failing; *no answer* is a timeout or a dropped connection.
- **Per page** — what a visitor waits for as the HTTP visitors replay it: the HTML, the page's data calls and the
  whole view. A view failed when any of its requests did.
- **In a browser** — the figures above, per page. Under `visitors` they are taken during the hold, alongside the
  load; compare them with a `browser` run at rest to see what the load costs a visitor.
- **Per route** — where the latency and the failures are.

Every error class has a counterpart in the admin Logs page: `RequestFailed` (an API 5xx), `RateLimitRejected`,
`RequestAborted` (a client gave up), `FrontendServerError` and `FrontendUpstreamErrors` (the web tier) and
`LogRecordsDropped` (the log channel itself overflowed) — #1555, #1556. A class k6 counted with no matching rows is
a gap in the logging, not in the site.

Host-side numbers — CPU and memory per container, pgbouncer pool wait, slow queries, restarts — are not in the
summary: they are sampled on the host while the test runs.
