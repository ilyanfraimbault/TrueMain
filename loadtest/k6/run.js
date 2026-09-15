// TrueMain load test — see docs/load-testing.md.
//
//   k6 run -e BASE_URL=http://<host>:3001 -e SCENARIO=visitors loadtest/k6/run.js
//
// SCENARIO
//   smoke            1 visitor for 1 minute, short think time: checks the script,
//                    the target and the summary before a real run.
//   visitors         ramps to VUS concurrent visitors over RAMP, holds for HOLD,
//                    ramps down over RAMP_DOWN. The capacity test.
//   ratelimit-probe  one client over the rate limit for a minute: proves 429s
//                    reach the ops logs, keyed on the client's address.
//
// Other variables: THINK_MIN / THINK_MAX (seconds between page views),
// FETCH_ASSETS (true|false), SUMMARY_DIR (where summary.md / summary.json go).

import http from 'k6/http'
import { fail, sleep } from 'k6'
import { between } from './lib/params.js'
import { ROUTES, newSession, record, viewPage } from './lib/journeys.js'
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
  'ratelimit-probe': {
    executor: 'constant-arrival-rate',
    rate: 6,
    timeUnit: '1s',
    duration: '1m',
    preAllocatedVUs: 4,
    maxVUs: 12,
    exec: 'probe',
  },
}

if (!SCENARIOS[SCENARIO]) {
  throw new Error(`Unknown SCENARIO "${SCENARIO}"; expected one of ${Object.keys(SCENARIOS).join(', ')}`)
}

function thresholds() {
  // Per-route entries always pass; they only make k6 keep the per-route numbers
  // the summary prints.
  const perRoute = {}
  for (const kind of Object.keys(ROUTES)) {
    for (const name of ROUTES[kind]) {
      perRoute[`http_reqs{name:${name}}`] = ['count>=0']
      perRoute[`http_req_failed{name:${name}}`] = ['rate>=0']
      perRoute[`http_req_duration{name:${name}}`] = ['max>=0']
    }
  }
  if (SCENARIO === 'ratelimit-probe') return perRoute
  const correctness = {
    'http_req_failed{kind:page}': ['rate<0.01'],
    'http_req_failed{kind:api}': ['rate<0.01'],
    'checks': ['rate>0.99'],
  }
  // Latency is judged under load only. A smoke run is one visitor on cold
  // caches: it proves the script and the target, and its first champion reads
  // are slow by construction, which says nothing about capacity.
  if (SCENARIO === 'smoke') return { ...perRoute, ...correctness }
  return {
    ...perRoute,
    ...correctness,
    'http_req_duration{kind:page}': ['p(95)<1500'],
    'http_req_duration{kind:api}': ['p(95)<2000'],
  }
}

export const options = {
  scenarios: { [SCENARIO]: SCENARIOS[SCENARIO] },
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

export function probe() {
  const page = 1 + Math.floor(Math.random() * 5)
  record(http.get(`${BASE_URL}/truemains?page=${page}`, { tags: { name: '/truemains', kind: 'page' } }))
  record(http.get(`${BASE_URL}/api/champions/overview`, { tags: { name: '/api/champions/overview', kind: 'api' } }))
}

export function handleSummary(data) {
  const profile = {
    scenario: SCENARIO,
    vus: SCENARIO === 'visitors' ? VUS : (SCENARIO === 'smoke' ? 1 : null),
    ramp: SCENARIO === 'visitors' ? RAMP : null,
    hold: SCENARIO === 'visitors' ? HOLD : null,
    rampDown: SCENARIO === 'visitors' ? RAMP_DOWN : null,
    thinkSeconds: SCENARIO === 'ratelimit-probe' ? null : [THINK_MIN, THINK_MAX],
    fetchAssets: FETCH_ASSETS,
    description: SCENARIO === 'visitors'
      ? `0 → ${VUS} visitors over ${RAMP}, held ${HOLD}, down over ${RAMP_DOWN}; ${THINK_MIN}–${THINK_MAX} s between page views; assets ${FETCH_ASSETS ? 'on' : 'off'}`
      : SCENARIO === 'smoke'
        ? `1 visitor for 1 minute; ${THINK_MIN}–${THINK_MAX} s between page views; assets ${FETCH_ASSETS ? 'on' : 'off'}`
        : '1 client at 12 requests/s for 1 minute, over the per-visitor rate limit',
  }
  const summary = buildSummary(data, profile, ROUTES)
  return {
    stdout: `${summary.markdown}\n`,
    [`${SUMMARY_DIR}/summary.md`]: summary.markdown,
    [`${SUMMARY_DIR}/summary.json`]: summary.json,
  }
}
