// The same pages loaded by a real browser (k6/browser, Chromium).
//
// The HTTP visitors in journeys.js create the load but cannot say how long a
// visitor waits: they do not run the site's JavaScript, and most of what a page
// shows — the champion page's data sections and its hundreds of `/_ipx` icons —
// only exists once that JavaScript has fetched it. A few browser VUs ride along
// the load and time each page from navigation start:
//
//   TTFB, LCP     first byte of the HTML, largest contentful paint;
//   data ready    the last same-origin `/api/*` answer;
//   view loaded   the landing viewport settled: no same-origin request in flight,
//                 last resource answered (images included);
//   page loaded   the whole page scrolled through, then every rendered image still
//                 loading brought into view in turn, until the network is quiet
//                 again — the deferred sections (`hydrate-on-visible`) and lazy
//                 images included. Scrolling is paced, so it carries a few seconds
//                 of the browser's own time. An image still not loaded after being
//                 brought into view (a lazy image clipped inside its container
//                 never starts) is counted apart, as never loaded.
//
// Each page load is a first visit: a fresh browser context, empty cache. Nothing
// leaves the origin (analytics requests are aborted), and every figure is tagged
// with the page template, never with a URL.

import { browser } from 'k6/browser'
import { Counter, Rate, Trend } from 'k6/metrics'
import { DEFAULT_ELO_BRACKET, pick, pickEloBracket, pickPosition, query } from './params.js'

export const browserMetrics = {
  ttfb: new Trend('browser_ttfb', true),
  lcp: new Trend('browser_lcp', true),
  dataReady: new Trend('browser_data_ready', true),
  viewLoaded: new Trend('browser_view_loaded', true),
  pageLoaded: new Trend('browser_page_loaded', true),
  images: new Counter('browser_images'),
  imagesFailed: new Counter('browser_images_failed'),
  imagesUnloaded: new Counter('browser_images_unloaded'),
  incomplete: new Rate('browser_page_incomplete'),
}

// A page that has not loaded within this budget counts as incomplete.
const PAGE_BUDGET_MS = 60000
const QUIET_MS = 750
const POLL_MS = 100
const MAX_REVEALS = 40

// Runs before the page's own scripts:
// - a resource-timing buffer large enough for a champion page (the default keeps 250 entries);
// - an LCP observer, since the browser only reports LCP to observers registered early;
// - a count of the page's own fetches in flight. It is kept in the page rather than
//   followed through k6's request events, whose handlers keep the iteration from ending.
const INIT_SCRIPT = `
  performance.setResourceTimingBufferSize(10000);
  window.__loadtestLcp = null;
  new PerformanceObserver(list => {
    for (const entry of list.getEntries()) window.__loadtestLcp = entry.startTime;
  }).observe({ type: 'largest-contentful-paint', buffered: true });
  window.__loadtestFetches = 0;
  const nativeFetch = window.fetch.bind(window);
  window.fetch = (input, init) => {
    const url = new URL(typeof input === 'string' || input instanceof URL ? input : input.url, location.href);
    if (url.origin !== location.origin) return nativeFetch(input, init);
    window.__loadtestFetches++;
    return nativeFetch(input, init).finally(() => { window.__loadtestFetches--; });
  };
`

function escapeRegExp(text) {
  return text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
}

// Where the page stands: requests of its own in flight, images within the
// viewport still loading, and how many resources it has fetched so far.
function progress() {
  const visible = Array.from(document.images).filter((image) => {
    const box = image.getBoundingClientRect()
    return image.getClientRects().length > 0 && box.bottom > 0 && box.top < window.innerHeight
  })
  return {
    fetches: window.__loadtestFetches || 0,
    pendingImages: visible.filter(image => !image.complete).length,
    resources: performance.getEntriesByType('resource').length,
    ready: document.readyState === 'complete',
  }
}

function measure() {
  const origin = location.origin
  const entries = performance.getEntriesByType('resource').filter(entry => entry.name.startsWith(`${origin}/`))
  const navigation = performance.getEntriesByType('navigation')[0]
  let loaded = navigation ? navigation.loadEventEnd : 0
  let dataReady = null
  for (const entry of entries) {
    loaded = Math.max(loaded, entry.responseEnd)
    if (entry.name.startsWith(`${origin}/api/`)) dataReady = Math.max(dataReady || 0, entry.responseEnd)
  }
  const rendered = Array.from(document.images).filter(image => image.getClientRects().length > 0)
  return {
    ttfb: navigation ? navigation.responseStart : null,
    lcp: window.__loadtestLcp,
    dataReady,
    loaded,
    images: rendered.length,
    pending: rendered.filter(image => !image.complete).length,
    failed: rendered.filter(image => image.complete && image.naturalWidth === 0).length,
  }
}

async function scrollThrough() {
  const step = Math.max(200, Math.floor(window.innerHeight * 0.8))
  for (let y = 0; y < document.documentElement.scrollHeight; y += step) {
    window.scrollTo(0, y)
    await new Promise(resolve => setTimeout(resolve, 200))
  }
  window.scrollTo(0, document.documentElement.scrollHeight)
}

// Brings the n-th rendered image still loading into view, and returns how many
// are still loading: a lazy image the paced scroll passed too quickly never
// starts otherwise.
function revealPendingImage(index) {
  const pending = Array.from(document.images)
    .filter(image => image.getClientRects().length > 0 && !image.complete)
  if (pending.length > index) pending[index].scrollIntoView({ block: 'center' })
  return pending.length
}

function pagePaths(data) {
  return [
    ['/champions/[slug]', () => {
      const [, slug] = pick(data.champions)
      const eloBracket = pickEloBracket()
      return `/champions/${slug}${query({ position: pickPosition(), elo: eloBracket === DEFAULT_ELO_BRACKET ? undefined : eloBracket })}`
    }],
    ['/', () => '/'],
    ['/champions/tierlist', () => `/champions/tierlist${query({ position: pickPosition() })}`],
    ['/champions', () => '/champions'],
    ['/truemains', () => '/truemains'],
    ['/truemains/[nameTag]', () => (data.nameTags.length > 0 ? `/truemains/${encodeURIComponent(pick(data.nameTags))}` : null)],
  ]
}

// Quiet: nothing of the page's own in flight, no image loading in the viewport,
// and no new resource for QUIET_MS.
async function waitForQuiet(tab, deadline) {
  let quietSince = 0
  let resources = -1
  while (Date.now() < deadline) {
    const state = await tab.evaluate(progress)
    if (state.ready && state.fetches === 0 && state.pendingImages === 0 && state.resources === resources) {
      if (quietSince === 0) quietSince = Date.now()
      if (Date.now() - quietSince >= QUIET_MS) return true
    }
    else {
      quietSince = 0
    }
    resources = state.resources
    await tab.waitForTimeout(POLL_MS)
  }
  return false
}

async function loadPage(base, page, path) {
  const tags = { page }
  const context = await browser.newContext()
  let tab = null
  let complete = false
  try {
    await context.addInitScript(INIT_SCRIPT)
    tab = await context.newPage()
    await tab.route(new RegExp(`^https?://(?!${escapeRegExp(base.replace(/^https?:\/\//, ''))}/)`), route => route.abort())

    const deadline = Date.now() + PAGE_BUDGET_MS
    const response = await tab.goto(`${base}${path}`, { waitUntil: 'load', timeout: PAGE_BUDGET_MS })
    if (!response || (response.status() >= 400 && response.status() !== 404)) return

    if (!(await waitForQuiet(tab, deadline))) return
    const landing = await tab.evaluate(measure)
    if (landing.ttfb !== null) browserMetrics.ttfb.add(landing.ttfb, tags)
    if (landing.lcp !== null) browserMetrics.lcp.add(landing.lcp, tags)
    if (landing.dataReady !== null) browserMetrics.dataReady.add(landing.dataReady, tags)
    browserMetrics.viewLoaded.add(landing.loaded, tags)

    await tab.evaluate(scrollThrough)
    if (!(await waitForQuiet(tab, deadline))) return
    for (let reveal = 0; reveal < MAX_REVEALS; reveal++) {
      if ((await tab.evaluate(revealPendingImage, reveal)) <= reveal) break
      if (!(await waitForQuiet(tab, deadline))) return
    }
    const whole = await tab.evaluate(measure)
    browserMetrics.pageLoaded.add(whole.loaded, tags)
    browserMetrics.images.add(whole.images, tags)
    browserMetrics.imagesFailed.add(whole.failed, tags)
    browserMetrics.imagesUnloaded.add(whole.pending, tags)
    complete = true
  }
  catch {
    // A navigation timeout or a crashed tab is an incomplete page, counted below.
  }
  finally {
    browserMetrics.incomplete.add(!complete, tags)
    // The tab first: a context closed over an open tab keeps the iteration from ending.
    if (tab) await tab.close()
    await context.close()
  }
}

/** One pass over every page template, each loaded as a first visit. */
export async function browsePages(base, data) {
  for (const [page, path] of pagePaths(data)) {
    const target = path()
    if (target === null) continue
    await loadPage(base, page, target)
  }
}
