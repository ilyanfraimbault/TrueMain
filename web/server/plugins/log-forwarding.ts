import { hostname } from 'node:os'

/**
 * Sends this server's errors to the ops logs (#1556): every request that fails
 * here with a 5xx — a render error, a handler that threw, the `/api` proxy unable
 * to reach the API — plus, from the proxy itself, each 429 or 5xx the API
 * answered (`server/api/[...path].ts`).
 *
 * Off unless `NUXT_LOG_INGEST_KEY` is set: it is the key the API expects on
 * `POST /internal/logs`. Twin of `admin/server/plugins/log-forwarding.ts`.
 */
export default defineNitroPlugin((nitroApp) => {
  const { apiBaseUrl, logIngestKey } = useRuntimeConfig()
  if (!logIngestKey) return

  const forwarder = createLogForwarder({
    process: 'Web',
    host: hostname(),
    send: batch => $fetch(`${apiBaseUrl}/internal/logs`, {
      method: 'POST',
      body: batch,
      headers: { 'X-Log-Ingest-Key': logIngestKey },
      timeout: 3_000,
      retry: 0,
    }),
    onSendError: error => console.error('[log-forwarder] could not forward errors to the API:', error instanceof Error ? error.message : error),
  })
  setLogForwarder(forwarder)

  const timer = setInterval(() => {
    void forwarder.flush()
  }, LOG_FORWARD_INTERVAL_MS)
  timer.unref?.()

  nitroApp.hooks.hook('error', (error, { event }) => {
    const statusCode = errorStatus(error)
    if (statusCode < 500) return
    reportToOpsLogs({
      level: 'Error',
      category: 'nitro',
      eventType: 'FrontendServerError',
      message: error.message,
      exception: error.stack ?? null,
      requestMethod: event?.method ?? null,
      requestPath: toRouteTemplate(event?.path),
      statusCode,
    })
  })

  nitroApp.hooks.hook('close', async () => {
    clearInterval(timer)
    setLogForwarder(null)
    await forwarder.flush()
  })
})
