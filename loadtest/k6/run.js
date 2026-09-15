// TrueMain load test — see docs/load-testing.md.
//
//   k6 run -e BASE_URL=http://<host>:3001 -e SCENARIO=visitors loadtest/k6/run.js
//
// SCENARIO
//   smoke            1 visitor for 1 minute, short think time, and one browser pass
//                    over every page: checks the script, the target and the
//                    summary before a real run.
//   visitors         ramps to VUS concurrent visitors over RAMP, holds for HOLD,
//                    ramps down over RAMP_DOWN. The capacity test. BROWSER_VUS
//                    browsers time full page loads during the hold.
//   browser          BROWSER_VUS browsers, BROWSER_PASSES passes over every page,
//                    no other load: the page-load reference at rest.
//   ratelimit-probe  one client well over the rate limit for a minute: proves
//                    429s reach the ops logs, keyed on the client's address,
//                    and that server-rendered pages spend the same budget.
//
// Other variables: THINK_MIN / THINK_MAX (seconds between page views),
// FETCH_ASSETS (true|false), SUMMARY_DIR (where summary.md / summary.json go).
// Browsers need Chromium: K6_BROWSER_EXECUTABLE_PATH when it is not on the PATH.

import http from 'k6/http'
import exec from 'k6/execution'
import { fail, sleep } from 'k6'
import { between } from './lib/params.js'
import { ROUTES, newSession, record, viewPage } from './lib/journeys.js'
import { browsePages } from './lib/browser.js'
import { buildSummary } from './lib/summary.js'

const BASE_URL = (__ENV.BASE_URL || '').replace(/\/+$/, '')
const SCENARIO = __ENV.SCENARIO || 'smoke'
const VUS = Number(__ENV.VUS || 200)
const RAMP = __ENV.RAMP || '3m'
const HOLD = __ENV.HOLD || '10m'
const RAMP_DOWN = __ENV.RAMP_DOWN || '2m'
const FETCH_ASSETS = (__ENV.FETCH_ASSETS || 'true') === 'true'
const SUMMARY_DIR = (__ENV.SUMMARY_DIR || '.').replace(/\/+$/, '')
const THINK_MIN = Number(__ENV.THINK_MIN || (SCENARIO === 'smoke' ? 1 : 5))
const THINK_MAX = Number(__ENV.THINK_MAX || (SCENARIO === 'smoke' ? 3 : 20))
const DEFAULT_BROWSER_VUS = { smoke: 1, visitors: 3, browser: 1 }
const BROWSER_VUS = Number(__ENV.BROWSER_VUS || DEFAULT_BROWSER_VUS[SCENARIO] || 0)
const BROWSER_PASSES = Number(__ENV.BROWSER_PASSES || 3)

if (!BASE_URL) {
  throw new Error('BASE_URL is required, e.g. -e BASE_URL=http://localhost:3001')
}

// A 404 is a champion without data for the slice — an answer, not a failure.
http.setResponseCallback(http.expectedStatuses({ min: 200, max: 399 }, 404))

const SCENARIOS = {
  smoke: {
    executor: 'constant-vus',
    vus: 1,
    duration: '1m',
    exec: 'visitors',
  },
  visitors: {
    executor: 'ramping-vus',
    startVUs: 0,
    stages: [
      { duration: RAMP, target: VUS },
      { duration: HOLD, target: VUS },
      { duration: RAMP_DOWN, target: 0 },
    ],
    gracefulRampDown: '30s',
    exec: 'visitors',
  },
  // The rate is set against a fast, cached API read so the limit is crossed
  // whatever the site's latency: a probe paced by slow server-rendered pages
  // can stay under the limit and prove nothing.
  'ratelimit-probe': {
    executor: 'constant-arrival-rate',
    rate: 16,
    timeUnit: '1s',
    duration: '1m',
    preAllocatedVUs: 20,
    maxVUs: 60,
    exec: 'probe',
  },
  browser: null,
}

if (!(SCENARIO in SCENARIOS)) {
  throw new Error(`Unknown SCENARIO "${SCENARIO}"; expected one of ${Object.keys(SCENARIOS).join(', ')}`)
}

// Browsers time pages while the HTTP visitors create the load: during the hold
// only, so a figure is never taken on a ramp.
function browserScenario() {
  if (BROWSER_VUS <= 0) return {}
  const shared = { exec: 'pages', options: { browser: { type: 'chromium' } } }
  if (SCENARIO === 'visitors') {
    return { browser: { ...shared, executor: 'constant-vus', vus: BROWSER_VUS, startTime: RAMP, duration: HOLD, gracefulStop: '2m' } }
  }
  if (SCENARIO === 'smoke') {
    return { browser: { ...shared, executor: 'per-vu-iterations', vus: BROWSER_VUS, iterations: 1, maxDuration: '10m' } }
  }
  if (SCENARIO === 'browser') {
    return { browser: { ...shared, executor: 'per-vu-iterations', vus: BROWSER_VUS, iterations: BROWSER_PASSES, maxDuration: '30m' } }
  }
  return {}
}

function thresholds() {
  // Per-route and per-page entries always pass; they only make k6 keep the
  // sub-metrics the summary prints.
  const kept = {}
  const keep = (key, expression) => { kept[key] = [...(kept[key] || []), expression] }
  for (const kind of Object.keys(ROUTES)) {
    for (const name of ROUTES[kind]) {
      keep(`http_reqs{name:${name}}`, 'count>=0')
      keep(`http_req_failed{name:${name}}`, 'rate>=0')
      keep(`http_req_duration{name:${name}}`, 'max>=0')
    }
  }
  for (const page of ROUTES.page) {
    for (const metric of ['page_html_duration', 'page_data_duration', 'page_view_duration']) keep(`${metric}{page:${page}}`, 'max>=0')
    keep(`page_view_failed{page:${page}}`, 'rate>=0')
    for (const metric of ['browser_ttfb', 'browser_lcp', 'browser_data_ready', 'browser_view_loaded', 'browser_page_loaded']) keep(`${metric}{page:${page}}`, 'max>=0')
    keep(`browser_images{page:${page}}`, 'count>=0')
    keep(`browser_images_failed{page:${page}}`, 'count>=0')
    keep(`browser_images_unloaded{page:${page}}`, 'count>=0')
    keep(`browser_page_incomplete{page:${page}}`, 'rate>=0')
  }
  if (SCENARIO === 'ratelimit-probe') return kept

  // Latency is judged under load or in the browser reference only. A smoke run
  // is one visitor on cold caches: it proves the script and the target, and its
  // first champion reads are slow by construction, which says nothing about
  // capacity.
  if (BROWSER_VUS > 0) {
    keep('browser_page_incomplete', 'rate<0.01')
    if (SCENARIO !== 'smoke') {
      keep('browser_page_loaded{page:/champions/[slug]}', 'p(95)<6000')
      keep('browser_data_ready', 'p(95)<3000')
      keep('browser_lcp', 'p(95)<2500')
    }
  }
  if (SCENARIO === 'browser') return kept

  keep('http_req_failed{kind:page}', 'rate<0.01')
  keep('http_req_failed{kind:api}', 'rate<0.01')
  keep('checks', 'rate>0.99')
  if (SCENARIO === 'visitors') {
    keep('http_req_duration{kind:page}', 'p(95)<1500')
    keep('http_req_duration{kind:api}', 'p(95)<2000')
  }
  return kept
}

export const options = {
  scenarios: { ...(SCENARIOS[SCENARIO] ? { [SCENARIO]: SCENARIOS[SCENARIO] } : {}), ...browserScenario() },
  thresholds: thresholds(),
  summaryTrendStats: ['avg', 'min', 'med', 'p(90)', 'p(95)', 'p(99)', 'max', 'count'],
  // Browser-like connection reuse and parallelism.
  batchPerHost: 6,
  userAgent: 'TrueMain-load-test/1.0 (k6)',
}

export function setup() {
  const slugs = http.get(`${BASE_URL}/api/static/champion-slugs`, { tags: { name: 'setup', kind: 'setup' } })
  const champions = slugs.status === 200
    ? Object.keys(slugs.json()).map(id => [Number(id), slugs.json()[id]])
    : []
  if (champions.length === 0) {
    fail(`champion slugs unavailable (status ${slugs.status}); is the site up?`)
  }

  const board = http.get(`${BASE_URL}/api/truemains?page=1&pageSize=25`, { tags: { name: 'setup', kind: 'setup' } })
  const rows = board.status === 200 ? (board.json().rows || []) : []
  const nameTags = rows
    .map(row => row.identity)
    .filter(identity => identity && identity.gameName && identity.tagLine)
    .map(identity => `${identity.gameName}-${identity.tagLine}`)
  if (nameTags.length === 0) {
    console.warn(`no player on the leaderboard (status ${board.status}): profile views fall back to /truemains`)
  }

  return { champions, nameTags }
}

export function visitors(data) {
  const ctx = { base: BASE_URL, data, fetchAssets: FETCH_ASSETS }
  const session = newSession()
  const views = 3 + Math.floor(Math.random() * 4)
  for (let view = 0; view < views; view++) {
    viewPage(ctx, session)
    sleep(between(THINK_MIN, THINK_MAX))
  }
}

export async function pages(data) {
  await browsePages(BASE_URL, data)
}

export function probe() {
  // One iteration in sixteen is a server-rendered page: its own call to the API
  // must land in the same visitor's budget, and be rejected with the rest once
  // it is spent.
  if (exec.scenario.iterationInTest % 16 === 0) {
    const page = 1 + Math.floor(Math.random() * 5)
    record(http.get(`${BASE_URL}/truemains?page=${page}`, { tags: { name: '/truemains', kind: 'page' } }))
    return
  }
  record(http.get(`${BASE_URL}/api/truemains?page=1&pageSize=5`, { tags: { name: '/api/truemains', kind: 'api' } }))
}

function describe() {
  const browsers = BROWSER_VUS > 0
    ? `; ${BROWSER_VUS} browser${BROWSER_VUS > 1 ? 's' : ''} timing full page loads`
    : ''
  if (SCENARIO === 'visitors') {
    return `0 → ${VUS} visitors over ${RAMP}, held ${HOLD}, down over ${RAMP_DOWN}; ${THINK_MIN}–${THINK_MAX} s between page views; assets ${FETCH_ASSETS ? 'on' : 'off'}${BROWSER_VUS > 0 ? `${browsers} during the hold` : ''}`
  }
  if (SCENARIO === 'smoke') {
    return `1 visitor for 1 minute; ${THINK_MIN}–${THINK_MAX} s between page views; assets ${FETCH_ASSETS ? 'on' : 'off'}${BROWSER_VUS > 0 ? `${browsers}, one pass` : ''}`
  }
  if (SCENARIO === 'browser') {
    return `${BROWSER_VUS} browser${BROWSER_VUS > 1 ? 's' : ''}, ${BROWSER_PASSES} passes over every page, no other load`
  }
  return '1 client at 16 requests/s for 1 minute (15 cached API reads, 1 server-rendered page), over the per-visitor rate limit'
}

export function handleSummary(data) {
  const profile = {
    scenario: SCENARIO,
    vus: SCENARIO === 'visitors' ? VUS : (SCENARIO === 'smoke' ? 1 : null),
    ramp: SCENARIO === 'visitors' ? RAMP : null,
    hold: SCENARIO === 'visitors' ? HOLD : null,
    rampDown: SCENARIO === 'visitors' ? RAMP_DOWN : null,
    thinkSeconds: SCENARIO === 'ratelimit-probe' || SCENARIO === 'browser' ? null : [THINK_MIN, THINK_MAX],
    fetchAssets: FETCH_ASSETS,
    browserVus: BROWSER_VUS,
    browserPasses: SCENARIO === 'browser' ? BROWSER_PASSES : null,
    description: describe(),
  }
  const summary = buildSummary(data, profile, ROUTES)
  return {
    stdout: `${summary.markdown}\n`,
    [`${SUMMARY_DIR}/summary.md`]: summary.markdown,
    [`${SUMMARY_DIR}/summary.json`]: summary.json,
  }
}
