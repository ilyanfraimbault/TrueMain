// End-of-test summary: a markdown table per route template for the job page, and
// a JSON of the same numbers for the artifact. Both are built from metrics only
// — no URL, no setup data (it carries player names), no host.

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

function thresholdRows(data) {
  const rows = []
  for (const key of Object.keys(data.metrics)) {
    // Per-route thresholds exist only to make k6 keep the sub-metrics; they are not verdicts.
    if (key.includes('{name:')) continue
    const thresholds = data.metrics[key].thresholds || {}
    for (const expression of Object.keys(thresholds)) {
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
