import { defineEventHandler, setResponseHeader, setResponseStatus } from 'h3'

// What the edge probes to decide on the maintenance page (#2407, #2416): this request
// covers web, and the API's own `/readyz` covers the API, PgBouncer and Postgres. It
// answers before the edge's 10 s `health_timeout`, so a slow API is counted here too.
const API_CHECK_TIMEOUT_MS = 8000

const gate = createReadinessGate()

export default defineEventHandler(async (event) => {
  const { apiBaseUrl } = useRuntimeConfig(event)
  const status = await $fetch.raw(`${apiBaseUrl}/readyz`, {
    timeout: API_CHECK_TIMEOUT_MS,
    retry: false,
    ignoreResponseError: true,
  }).then(response => response.status, () => null)

  const up = status === 200
  if (!up) {
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
  return 'ok'
})
