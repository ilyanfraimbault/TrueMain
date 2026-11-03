import { defineEventHandler, setResponseHeader, setResponseStatus } from 'h3'

// What the edge probes to decide on the maintenance page (#2407, #2416): this request
// covers web, and the API's own `/readyz` covers the API, PgBouncer and Postgres. It
// answers before the edge's 10 s `health_timeout`, so a slow API is counted here too.
const API_CHECK_TIMEOUT_MS = 8000

const gate = createReadinessGate()

export default defineEventHandler(async (event) => {
  const { apiBaseUrl, warmupEnabled, warmupBudgetSeconds } = useRuntimeConfig(event)
  const response = await $fetch.raw<string>(`${apiBaseUrl}/readyz`, {
    timeout: API_CHECK_TIMEOUT_MS,
    retry: false,
    ignoreResponseError: true,
  }).catch(() => null)
  const status = response?.status ?? null

  const up = status === 200
  // Not ready, not a failure (#2425): the site stays on the maintenance page, nothing to report.
  if (!up && !isApiWarming(status, response?._data)) {
    reportToOpsLogs({
      level: 'Error',
      category: 'api-proxy',
      eventType: 'FrontendUpstreamErrors',
      message: `The API's readiness check failed (${status ?? 'no answer'})`,
      requestMethod: 'GET',
      requestPath: '/api/readyz',
      statusCode: status,
    })
  }

  setResponseHeader(event, 'Cache-Control', 'no-store')
  if (!gate.record(up)) {
    setResponseStatus(event, 503)
    return 'down'
  }
  // This answer speaks for every worker, and they warm one by one (`plugins/warmup.ts`).
  if (warmupEnabled && !isSiteWarm(clusterWorkers(), warmupBudgetSeconds)) {
    setResponseStatus(event, 503)
    return 'warming'
  }
  return 'ok'
})
