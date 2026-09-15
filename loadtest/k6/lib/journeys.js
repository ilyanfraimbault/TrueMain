// What one simulated visitor does.
//
// k6 does not run the site's JavaScript, so a page view is replayed by hand: the
// server-rendered HTML first (which exercises SSR and the calls it makes), then
// the `/api/*` calls the browser would make once the page hydrates. The lists
// below mirror the page composables in `web/app`; keep them in step when a page
// starts or stops fetching something.
//
// Every request is tagged with a route template (`name`) and a `kind`, never
// with its URL, so summaries group by route and never carry the host.

import http from 'k6/http'
import { check } from 'k6'
import { Counter } from 'k6/metrics'
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
  ],
  asset: ['/_nuxt/[asset]', '/_ipx/[image]'],
}

function page(ctx, session, path, name) {
  const response = http.get(`${ctx.base}${path}`, { tags: { name, kind: 'page' } })
  record(response)
  check(response, { 'page answered': answered }, { kind: 'page' })
  if (ctx.fetchAssets && !session.assetsLoaded && response.status === 200) {
    session.assetsLoaded = true
    assets(ctx, String(response.body || ''))
  }
  return response
}

function batch(ctx, kind, calls) {
  if (calls.length === 0) return []
  const requests = calls.map(([path, name]) => ['GET', `${ctx.base}${path}`, null, { tags: { name, kind } }])
  const responses = http.batch(requests)
  for (const response of responses) {
    record(response)
    check(response, { [`${kind} answered`]: answered }, { kind })
  }
  return responses
}

// The build the page opens on: its first item and keystone key the power-spike
// read, which the API refuses without them. Null when the slice has no build.
function topBuild(response) {
  if (response.status !== 200) return null
  try {
    const build = (response.json('builds') || [])[0]
    return build && build.firstItemId > 0 && build.primaryKeystoneId > 0 ? build : null
  }
  catch {
    return null
  }
}

// The browser keeps static lookups for an hour (`app/utils/static-cache.ts`), so
// a visitor pays each one once per session, not once per page.
function statics(ctx, session, names) {
  const calls = []
  for (const name of names) {
    if (session.statics[name]) continue
    session.statics[name] = true
    calls.push([`/api/static/${name}`, `/api/static/${name}`])
  }
  batch(ctx, 'static', calls)
}

// The page's own bundles and images, once per session, parsed from the first
// HTML response. Off when FETCH_ASSETS=false, to isolate SSR and API load.
function assets(ctx, html) {
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
  batch(ctx, 'asset', calls)
}

function home(ctx, session) {
  page(ctx, session, '/', '/')
  statics(ctx, session, ['champions', 'versions'])
  batch(ctx, 'api', [
    ['/api/champions/overview', '/api/champions/overview'],
    ['/api/truemains?page=1&pageSize=5', '/api/truemains'],
  ])
}

function championsList(ctx, session) {
  const eloBracket = pickEloBracket()
  page(ctx, session, `/champions${query({ elo: eloBracket === DEFAULT_ELO_BRACKET ? undefined : eloBracket })}`, '/champions')
  statics(ctx, session, ['champions', 'versions', 'items', 'rune-tree'])
  batch(ctx, 'api', [[`/api/champions${query({ eloBracket })}`, '/api/champions']])
}

function tierList(ctx, session) {
  const position = pickPosition()
  const eloBracket = pickEloBracket()
  page(ctx, session, `/champions/tierlist${query({ position })}`, '/champions/tierlist')
  statics(ctx, session, ['champions', 'versions'])
  batch(ctx, 'api', [[`/api/champions/tierlist${query({ position, eloBracket })}`, '/api/champions/tierlist']])
}

function championPage(ctx, session) {
  const [id, slug] = pick(ctx.data.champions)
  const position = pickPosition()
  const eloBracket = pickEloBracket()
  const slice = query({ position, eloBracket })
  page(ctx, session, `/champions/${slug}${query({ position, elo: eloBracket === DEFAULT_ELO_BRACKET ? undefined : eloBracket })}`, '/champions/[slug]')
  statics(ctx, session, ['items', 'rune-tree', 'summoner-spells', 'champions', 'versions'])
  // Duo trios are left out: the page fires them only once a visitor picks a
  // partner, never on load.
  const [champion] = batch(ctx, 'api', [
    [`/api/champions/${id}${slice}`, '/api/champions/[id]'],
    [`/api/champions/${id}/trend${query({ position })}`, '/api/champions/[id]/trend'],
    [`/api/champions/${id}/scaling${slice}`, '/api/champions/[id]/scaling'],
    [`/api/champions/${id}/roam${slice}`, '/api/champions/[id]/roam'],
    [`/api/champions/${id}/item-context${query({ position })}`, '/api/champions/[id]/item-context'],
    [`/api/champions/${id}/matchups${slice}`, '/api/champions/[id]/matchups'],
    [`/api/champions/${id}/synergies${slice}`, '/api/champions/[id]/synergies'],
  ])
  // Power spikes wait for the builds, as the build panel does.
  const build = topBuild(champion)
  if (build) {
    batch(ctx, 'api', [[
      `/api/champions/${id}/powerspikes${query({ position, eloBracket, buildFirstItemId: build.firstItemId, buildKeystoneId: build.primaryKeystoneId })}`,
      '/api/champions/[id]/powerspikes',
    ]])
  }
}

function truemainsList(ctx, session) {
  page(ctx, session, '/truemains', '/truemains')
  statics(ctx, session, ['champions', 'versions', 'rune-tree', 'items'])
}

function profile(ctx, session) {
  if (ctx.data.nameTags.length === 0) {
    truemainsList(ctx, session)
    return
  }
  const nameTag = encodeURIComponent(pick(ctx.data.nameTags))
  page(ctx, session, `/truemains/${nameTag}`, '/truemains/[nameTag]')
  statics(ctx, session, ['items', 'champions', 'versions'])
  batch(ctx, 'api', [
    [`/api/truemains/${nameTag}/profile`, '/api/truemains/[nameTag]/profile'],
    [`/api/truemains/${nameTag}/rank-history?days=90`, '/api/truemains/[nameTag]/rank-history'],
    [`/api/truemains/${nameTag}/activity`, '/api/truemains/[nameTag]/activity'],
    [`/api/truemains/${nameTag}/matches?page=1`, '/api/truemains/[nameTag]/matches'],
  ])
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
