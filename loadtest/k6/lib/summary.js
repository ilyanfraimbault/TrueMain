// End-of-test summary: markdown tables for the job page (per page, in a browser,
// per route) and a JSON of the same numbers for the artifact. Both are built from
// metrics only — no URL, no setup data (it carries player names), no host.

const RESPONSE_CLASSES = [
  ['responses_2xx', '2xx'],
  ['responses_3xx', '3xx'],
  ['responses_404', '404 (no data for the slice)'],
  ['responses_4xx_other', 'other 4xx'],
  ['responses_429', '429 (rate limited)'],
  ['responses_5xx', '5xx'],
  ['responses_no_answer', 'no answer (timeout, reset, refused)'],
]

function metricValue(data, key, stat) {
  const metric = data.metrics[key]
  return metric && metric.values ? metric.values[stat] : undefined
}

// k6's JavaScript runtime has no locale support (`toLocaleString` throws), so
// thousands are grouped by hand.
function grouped(value) {
  return String(Math.round(value)).replace(/\B(?=(\d{3})+(?!\d))/g, ',')
}

function ms(value) {
  return value === undefined ? '—' : `${grouped(value)} ms`
}

// A kept sub-metric with no sample still reports 0: show that as no figure.
function pair(stats) {
  return !stats.count ? '—' : `${ms(stats.med)} / ${ms(stats.p95)}`
}

function percent(value) {
  return value === undefined ? '—' : `${(value * 100).toFixed(2)} %`
}

function count(value) {
  return value === undefined ? '0' : grouped(value)
}

function routeRows(data, routes) {
  const rows = []
  for (const kind of Object.keys(routes)) {
    for (const name of routes[kind]) {
      const requests = metricValue(data, `http_reqs{name:${name}}`, 'count')
      if (!requests) continue
      rows.push({
        kind,
        name,
        requests,
        failedRate: metricValue(data, `http_req_failed{name:${name}}`, 'rate'),
        med: metricValue(data, `http_req_duration{name:${name}}`, 'med'),
        p95: metricValue(data, `http_req_duration{name:${name}}`, 'p(95)'),
        p99: metricValue(data, `http_req_duration{name:${name}}`, 'p(99)'),
        max: metricValue(data, `http_req_duration{name:${name}}`, 'max'),
      })
    }
  }
  return rows
}

function trend(data, key) {
  return {
    count: metricValue(data, key, 'count'),
    med: metricValue(data, key, 'med'),
    p95: metricValue(data, key, 'p(95)'),
  }
}

// What a visitor waits for, page by page, as the HTTP visitors replay it: the
// server-rendered HTML, the data calls the page makes, and the whole view.
function pageRows(data, pages) {
  const rows = []
  for (const page of pages) {
    const views = metricValue(data, `page_view_duration{page:${page}}`, 'count')
    if (!views) continue
    rows.push({
      page,
      views,
      failedRate: metricValue(data, `page_view_failed{page:${page}}`, 'rate'),
      html: trend(data, `page_html_duration{page:${page}}`),
      data: trend(data, `page_data_duration{page:${page}}`),
      view: trend(data, `page_view_duration{page:${page}}`),
    })
  }
  return rows
}

// The same pages loaded by a real browser, which runs the site's JavaScript and
// loads every image: see lib/browser.js for what each figure measures.
function browserRows(data, pages) {
  const rows = []
  for (const page of pages) {
    const loads = (metricValue(data, `browser_page_incomplete{page:${page}}`, 'passes') || 0)
      + (metricValue(data, `browser_page_incomplete{page:${page}}`, 'fails') || 0)
    if (!loads) continue
    rows.push({
      page,
      loads,
      incompleteRate: metricValue(data, `browser_page_incomplete{page:${page}}`, 'rate'),
      ttfb: trend(data, `browser_ttfb{page:${page}}`),
      lcp: trend(data, `browser_lcp{page:${page}}`),
      dataReady: trend(data, `browser_data_ready{page:${page}}`),
      viewLoaded: trend(data, `browser_view_loaded{page:${page}}`),
      pageLoaded: trend(data, `browser_page_loaded{page:${page}}`),
      images: metricValue(data, `browser_images{page:${page}}`, 'count'),
      imagesFailed: metricValue(data, `browser_images_failed{page:${page}}`, 'count'),
      imagesUnloaded: metricValue(data, `browser_images_unloaded{page:${page}}`, 'count'),
    })
  }
  return rows
}

function thresholdRows(data) {
  const rows = []
  for (const key of Object.keys(data.metrics)) {
    const thresholds = data.metrics[key].thresholds || {}
    for (const expression of Object.keys(thresholds)) {
      // `>=0` entries exist only to make k6 keep a sub-metric; they are not verdicts.
      if (expression.endsWith('>=0')) continue
      rows.push({ metric: key, expression, ok: thresholds[expression].ok })
    }
  }
  return rows
}

export function buildSummary(data, profile, routes) {
  const durationSeconds = data.state ? data.state.testRunDurationMs / 1000 : undefined
  const totalRequests = metricValue(data, 'http_reqs', 'count')
  const result = {
    scenario: profile.scenario,
    profile,
    durationSeconds,
    requests: totalRequests,
    requestsPerSecond: metricValue(data, 'http_reqs', 'rate'),
    iterations: metricValue(data, 'iterations', 'count'),
    maxVirtualUsers: metricValue(data, 'vus_max', 'max'),
    checksPassedRate: metricValue(data, 'checks', 'rate'),
    responses: Object.fromEntries(RESPONSE_CLASSES.map(([key, label]) => [label, metricValue(data, key, 'count') || 0])),
    thresholds: thresholdRows(data),
    pages: pageRows(data, routes.page),
    browser: browserRows(data, routes.page),
    routes: routeRows(data, routes),
  }
  return { json: JSON.stringify(result, null, 2), markdown: toMarkdown(result) }
}

function toMarkdown(result) {
  const lines = []
  const p = result.profile
  lines.push(`## TrueMain load test — \`${result.scenario}\``)
  lines.push('')
  lines.push(`Profile: ${p.description}. Ran ${result.durationSeconds === undefined ? '—' : `${Math.round(result.durationSeconds)} s`}, `
    + `${count(result.requests)} requests (${result.requestsPerSecond === undefined ? '—' : result.requestsPerSecond.toFixed(1)} req/s), `
    + `${count(result.iterations)} visitor sessions, up to ${count(result.maxVirtualUsers)} virtual visitors.`)
  lines.push('')
  if (result.thresholds.length > 0) {
    lines.push('### Thresholds')
    lines.push('')
    lines.push('| Metric | Threshold | Result |')
    lines.push('| --- | --- | --- |')
    for (const row of result.thresholds) {
      lines.push(`| \`${row.metric}\` | \`${row.expression}\` | ${row.ok ? 'pass' : '**crossed**'} |`)
    }
    lines.push('')
  }
  lines.push('### Responses')
  lines.push('')
  lines.push('| Class | Count |')
  lines.push('| --- | ---: |')
  for (const label of Object.keys(result.responses)) {
    lines.push(`| ${label} | ${count(result.responses[label])} |`)
  }
  lines.push('')
  if (result.pages.length > 0) {
    lines.push('### Per page')
    lines.push('')
    lines.push('Replayed over HTTP: *HTML* is the server-rendered page, *data* the API calls the page makes once hydrated, *view* both plus bundles and images. Median / p95.')
    lines.push('')
    lines.push('| Page | Views | Failed | HTML | Data | View |')
    lines.push('| --- | ---: | ---: | ---: | ---: | ---: |')
    for (const row of result.pages) {
      lines.push(`| \`${row.page}\` | ${count(row.views)} | ${percent(row.failedRate)} | ${pair(row.html)} | ${pair(row.data)} | ${pair(row.view)} |`)
    }
    lines.push('')
  }
  if (result.browser.length > 0) {
    lines.push('### In a browser')
    lines.push('')
    lines.push('Chromium running the site: *data ready* is the last API answer, *view loaded* the landing viewport with its images, *page loaded* the whole page scrolled through with every rendered image, all from navigation start. Median / p95.')
    lines.push('')
    lines.push('| Page | Loads | Incomplete | TTFB | LCP | Data ready | View loaded | Page loaded | Images | Failed / never loaded |')
    lines.push('| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |')
    for (const row of result.browser) {
      lines.push(`| \`${row.page}\` | ${count(row.loads)} | ${percent(row.incompleteRate)} | ${pair(row.ttfb)} | ${pair(row.lcp)} | ${pair(row.dataReady)} | ${pair(row.viewLoaded)} | ${pair(row.pageLoaded)} | ${count(row.images)} | ${count(row.imagesFailed)} / ${count(row.imagesUnloaded)} |`)
    }
    lines.push('')
  }
  lines.push('### Per route')
  lines.push('')
  lines.push('| Kind | Route | Requests | Failed | Median | p95 | p99 | Max |')
  lines.push('| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |')
  for (const row of result.routes) {
    lines.push(`| ${row.kind} | \`${row.name}\` | ${count(row.requests)} | ${percent(row.failedRate)} | ${ms(row.med)} | ${ms(row.p95)} | ${ms(row.p99)} | ${ms(row.max)} |`)
  }
  lines.push('')
  return lines.join('\n')
}
