// What one simulated visitor does.
//
// k6 does not run the site's JavaScript, so a page view is replayed by hand: the
// server-rendered HTML first (which exercises SSR and the calls it makes), then
// the `/api/*` calls the browser would make once the page hydrates. The lists
// below mirror the page composables in `web/app`; keep them in step when a page
// starts or stops fetching something.
//
// Every request is tagged with a route template (`name`) and a `kind`, never
// with its URL, so summaries group by route and never carry the host. Every page
// view is also timed as a whole, tagged with its page template (`page`): what a
// visitor waits for, where the per-route figures only say which call was slow.

import http from 'k6/http'
import { check } from 'k6'
import { Counter, Rate, Trend } from 'k6/metrics'
import { DEFAULT_ELO_BRACKET, pick, pickEloBracket, pickPosition, query, weighted } from './params.js'

// One counter per response class, so the summary tells a throttle (429) from an
// overload (5xx) from a request that never got an answer (status 0: timeout,
// reset, refused).
export const responses = {
  ok: new Counter('responses_2xx'),
  redirect: new Counter('responses_3xx'),
  notFound: new Counter('responses_404'),
  clientError: new Counter('responses_4xx_other'),
  throttled: new Counter('responses_429'),
  serverError: new Counter('responses_5xx'),
  noAnswer: new Counter('responses_no_answer'),
}

// Per page view: the server-rendered HTML, the data calls the page makes once
// hydrated (static lookups included, run as the browser runs them: in parallel,
// then the calls that wait on an answer), and the whole view with its assets.
// A view failed when any of its requests did.
export const pageTimings = {
  html: new Trend('page_html_duration', true),
  data: new Trend('page_data_duration', true),
  view: new Trend('page_view_duration', true),
  failed: new Rate('page_view_failed'),
}

function timedView(page, body) {
  const view = { page, started: Date.now(), htmlMs: 0, dataMs: 0, dataCalls: 0, failed: false }
  body(view)
  const tags = { page }
  pageTimings.html.add(view.htmlMs, tags)
  // A page rendered entirely on the server (the leaderboard) makes no data call.
  if (view.dataCalls > 0) pageTimings.data.add(view.dataMs, tags)
  pageTimings.view.add(Date.now() - view.started, tags)
  pageTimings.failed.add(view.failed, tags)
}

export function record(response) {
  const status = response.status
  if (status === 0) responses.noAnswer.add(1)
  else if (status === 429) responses.throttled.add(1)
  else if (status === 404) responses.notFound.add(1)
  else if (status >= 500) responses.serverError.add(1)
  else if (status >= 400) responses.clientError.add(1)
  else if (status >= 300) responses.redirect.add(1)
  else responses.ok.add(1)
}

// A champion with no aggregate for a slice answers 404 — the page shows "no
// data", which is a correct answer, not an error (see the response callback in
// run.js).
function answered(response) {
  return (response.status >= 200 && response.status < 400) || response.status === 404
}

export const ROUTES = {
  page: [
    '/',
    '/champions',
    '/champions/tierlist',
    '/champions/[slug]',
    '/truemains',
    '/truemains/[nameTag]',
  ],
  api: [
    '/api/champions/overview',
    '/api/champions',
    '/api/champions/tierlist',
    '/api/champions/[id]',
    '/api/champions/[id]/trend',
    '/api/champions/[id]/scaling',
    '/api/champions/[id]/roam',
    '/api/champions/[id]/powerspikes',
    '/api/champions/[id]/item-context',
    '/api/champions/[id]/matchups',
    '/api/champions/[id]/synergies',
    '/api/truemains',
    '/api/truemains/[nameTag]/profile',
    '/api/truemains/[nameTag]/rank-history',
    '/api/truemains/[nameTag]/activity',
    '/api/truemains/[nameTag]/matches',
  ],
  static: [
    '/api/static/champions',
    '/api/static/versions',
    '/api/static/items',
    '/api/static/rune-tree',
    '/api/static/summoner-spells',
    '/api/static/[id]',
  ],
  asset: ['/_nuxt/[asset]', '/_ipx/[image]'],
}

function page(ctx, session, view, path) {
  const response = http.get(`${ctx.base}${path}`, { tags: { name: view.page, kind: 'page' } })
  record(response)
  view.htmlMs = response.timings.duration
  if (!check(response, { 'page answered': answered }, { kind: 'page' })) view.failed = true
  if (ctx.fetchAssets && !session.assetsLoaded && response.status === 200) {
    session.assetsLoaded = true
    assets(ctx, view, String(response.body || ''))
  }
  return response
}

function batch(ctx, view, kind, calls) {
  if (calls.length === 0) return []
  const requests = calls.map(([path, name, callKind]) => ['GET', `${ctx.base}${path}`, null, { tags: { name, kind: callKind || kind } }])
  const started = Date.now()
  const responses = http.batch(requests)
  if (kind !== 'asset') {
    view.dataMs += Date.now() - started
    view.dataCalls += calls.length
  }
  responses.forEach((response, index) => {
    const callKind = calls[index][2] || kind
    record(response)
    if (!check(response, { [`${callKind} answered`]: answered }, { kind: callKind })) view.failed = true
  })
  return responses
}

// The builds the page renders as tabs. Every tab's panel stays mounted and fetches
// its own power spikes, keyed on the build's first item and keystone — the API
// refuses the read without them — so a visitor causes one power-spike request per
// build the champion read returned (already capped server-side).
function buildKeys(response) {
  if (response.status !== 200) return []
  try {
    return (response.json('builds') || [])
      .filter(build => build && build.firstItemId > 0 && build.primaryKeystoneId > 0)
  }
  catch {
    return []
  }
}

// The browser keeps static lookups for an hour (`app/utils/static-cache.ts`), so
// a visitor pays each one once per session, not once per page.
function staticCalls(session, names) {
  const calls = []
  for (const name of names) {
    if (session.statics[name]) continue
    session.statics[name] = true
    calls.push([`/api/static/${name}`, `/api/static/${name}`, 'static'])
  }
  return calls
}

// The page's own bundles and images, once per session, parsed from the first
// HTML response. Off when FETCH_ASSETS=false, to isolate SSR and API load.
function assets(ctx, view, html) {
  const seen = {}
  const calls = []
  const pattern = /(\/_nuxt\/[^"'\s)]+|\/_ipx\/[^"'\s),]+)/g
  let match = pattern.exec(html)
  while (match !== null && calls.length < 40) {
    const path = match[1].replace(/&amp;/g, '&')
    if (!seen[path]) {
      seen[path] = true
      calls.push([path, path.startsWith('/_nuxt/') ? '/_nuxt/[asset]' : '/_ipx/[image]'])
    }
    match = pattern.exec(html)
  }
  batch(ctx, view, 'asset', calls)
}

// A page's static lookups go out with its data calls, as the hydrated page sends
// them together.
function home(ctx, session) {
  timedView('/', (view) => {
    page(ctx, session, view, '/')
    batch(ctx, view, 'api', [
      ...staticCalls(session, ['champions', 'versions']),
      ['/api/champions/overview', '/api/champions/overview'],
      ['/api/truemains?page=1&pageSize=5', '/api/truemains'],
    ])
  })
}

function championsList(ctx, session) {
  const eloBracket = pickEloBracket()
  timedView('/champions', (view) => {
    page(ctx, session, view, `/champions${query({ elo: eloBracket === DEFAULT_ELO_BRACKET ? undefined : eloBracket })}`)
    batch(ctx, view, 'api', [
      ...staticCalls(session, ['champions', 'versions', 'items', 'rune-tree']),
      [`/api/champions${query({ eloBracket })}`, '/api/champions'],
    ])
  })
}

function tierList(ctx, session) {
  const position = pickPosition()
  const eloBracket = pickEloBracket()
  timedView('/champions/tierlist', (view) => {
    page(ctx, session, view, `/champions/tierlist${query({ position })}`)
    batch(ctx, view, 'api', [
      ...staticCalls(session, ['champions', 'versions']),
      [`/api/champions/tierlist${query({ position, eloBracket })}`, '/api/champions/tierlist'],
    ])
  })
}

function championPage(ctx, session) {
  const [id, slug] = pick(ctx.data.champions)
  const position = pickPosition()
  const eloBracket = pickEloBracket()
  const slice = query({ position, eloBracket })
  timedView('/champions/[slug]', (view) => {
    page(ctx, session, view, `/champions/${slug}${query({ position, elo: eloBracket === DEFAULT_ELO_BRACKET ? undefined : eloBracket })}`)
    // Duo trios are left out: the page fires them only once a visitor picks a
    // partner, never on load. The mains card is in: it loads once scrolled into
    // view, like the trend, scaling, matchup and synergy sections.
    const [champion] = batch(ctx, view, 'api', [
      [`/api/champions/${id}${slice}`, '/api/champions/[id]'],
      ...staticCalls(session, ['items', 'rune-tree', 'summoner-spells', 'champions', 'versions']),
      [`/api/champions/${id}/trend${query({ position })}`, '/api/champions/[id]/trend'],
      [`/api/champions/${id}/scaling${slice}`, '/api/champions/[id]/scaling'],
      [`/api/champions/${id}/roam${slice}`, '/api/champions/[id]/roam'],
      [`/api/champions/${id}/item-context${query({ position })}`, '/api/champions/[id]/item-context'],
      [`/api/champions/${id}/matchups${slice}`, '/api/champions/[id]/matchups'],
      [`/api/champions/${id}/synergies${slice}`, '/api/champions/[id]/synergies'],
      [`/api/truemains${query({ page: 1, pageSize: 10, championId: id })}`, '/api/truemains'],
    ])
    // Power spikes wait for the builds, then fire for the tab on screen only: a
    // build panel is mounted the first time its tab is opened (#1585). The
    // champion's static data waits for its patch.
    const patch = patchOf(champion)
    const staticKey = `champion-${id}-${patch}`
    const followUps = buildKeys(champion).slice(0, 1).map(build => [
      `/api/champions/${id}/powerspikes${query({ position, eloBracket, buildFirstItemId: build.firstItemId, buildKeystoneId: build.primaryKeystoneId })}`,
      '/api/champions/[id]/powerspikes',
    ])
    if (patch && !session.statics[staticKey]) {
      session.statics[staticKey] = true
      followUps.push([`/api/static/${id}${query({ patch })}`, '/api/static/[id]', 'static'])
    }
    batch(ctx, view, 'api', followUps)
  })
}

function patchOf(response) {
  if (response.status !== 200) return null
  try {
    return response.json('patch') || null
  }
  catch {
    return null
  }
}

function truemainsList(ctx, session) {
  timedView('/truemains', (view) => {
    page(ctx, session, view, '/truemains')
    batch(ctx, view, 'static', staticCalls(session, ['champions', 'versions', 'rune-tree', 'items']))
  })
}

function profile(ctx, session) {
  if (ctx.data.nameTags.length === 0) {
    truemainsList(ctx, session)
    return
  }
  const nameTag = encodeURIComponent(pick(ctx.data.nameTags))
  timedView('/truemains/[nameTag]', (view) => {
    page(ctx, session, view, `/truemains/${nameTag}`)
    batch(ctx, view, 'api', [
      ...staticCalls(session, ['items', 'champions', 'versions']),
      [`/api/truemains/${nameTag}/profile`, '/api/truemains/[nameTag]/profile'],
      [`/api/truemains/${nameTag}/rank-history?days=90`, '/api/truemains/[nameTag]/rank-history'],
      [`/api/truemains/${nameTag}/activity`, '/api/truemains/[nameTag]/activity'],
      [`/api/truemains/${nameTag}/matches?page=1`, '/api/truemains/[nameTag]/matches'],
    ])
  })
}

// Share of page views per journey. The champion page dominates because it is
// where search traffic lands.
const JOURNEYS = [
  [championPage, 40],
  [home, 20],
  [tierList, 15],
  [championsList, 10],
  [truemainsList, 10],
  [profile, 5],
]

/** Picks and runs one page view. */
export function viewPage(ctx, session) {
  weighted(JOURNEYS)(ctx, session)
}

export function newSession() {
  return { statics: {}, assetsLoaded: false }
}
