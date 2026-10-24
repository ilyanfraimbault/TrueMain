/**
 * Sends this server's errors to the ops logs (#1556): every request that fails
 * here with a 5xx — a render error, a handler that threw, a proxy (the site's
 * `/api`, the admin's `/api/ops`) unable to reach the API — plus, from the
 * proxies themselves, each 429 or 5xx the API answered.
 *
 * Off unless `NUXT_LOG_INGEST_KEY` is set: it is the key the API expects on
 * `POST /internal/logs`. Shared by both apps through `web/layers/nitro` (#1625);
 * what differs between them — the process name the errors are filed under and
 * the API they go to — comes from each app's `logForwardingTarget()`
 * (`server/utils/log-forwarding-target.ts`).
 */
export default defineNitroPlugin((nitroApp) => {
  const { logIngestKey } = useRuntimeConfig()
  if (!logIngestKey) return
  const target = logForwardingTarget()

  const { forwarder, stop } = startIngestForwarder({ process: target.process, apiBaseUrl: target.apiBaseUrl, logIngestKey })
  setLogForwarder(forwarder)

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

  // A client that left before this server answered (#1569), once per request and
  // counted per route like everything else here. Nothing else would ever report it:
  // an abandoned response is never written, so the error hook above does not run.
  nitroApp.hooks.hook('request', (event) => {
    if (!isReportableAbandonment(event.path)) return
    watchForAbandonment(event, () => {
      const route = toRouteTemplate(event.path)
      reportToOpsLogs({
        level: 'Warning',
        category: 'nitro',
        eventType: 'FrontendRequestAborted',
        message: `The client left before ${event.method} ${route} was answered`,
        requestMethod: event.method,
        requestPath: route,
      })
    })
  })

  nitroApp.hooks.hook('close', async () => {
    setLogForwarder(null)
    await stop()
  })
})
