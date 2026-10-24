/**
 * Relays the edge proxy's own failures to the ops logs (#1580), filed under
 * process `Edge`: the edge streams its error log to this port, and each 5xx it
 * generated itself — an upstream it could not dial, a response it could not read
 * — is counted per site, status and error through the same forwarder as this
 * server's own errors.
 *
 * Here rather than on the site because the site is what fails in the incident
 * this exists for (#1570); the admin is the other server on the edge's network.
 * Off unless both `NUXT_EDGE_LOG_PORT` and `NUXT_LOG_INGEST_KEY` are set. The
 * port is reachable from that network only, never published.
 */
export default defineNitroPlugin((nitroApp) => {
  const config = useRuntimeConfig()
  if (!config.edgeLogPort) return
  const port = edgeLogPort(config.edgeLogPort)
  if (port === null || !config.logIngestKey) {
    console.error(port === null
      ? `[edge-error-relay] off: NUXT_EDGE_LOG_PORT is not a port number (${String(config.edgeLogPort)})`
      : '[edge-error-relay] off: NUXT_EDGE_LOG_PORT is set but NUXT_LOG_INGEST_KEY is not')
    return
  }

  const { forwarder, stop } = startIngestForwarder({
    process: 'Edge',
    apiBaseUrl: config.opsApiBaseUrl,
    logIngestKey: config.logIngestKey,
  })

  const server = createLineServer((line) => {
    const report = toEdgeProxyError(line)
    if (report) forwarder.report(report)
  })
  server.on('error', error => console.error('[edge-error-relay] cannot receive the edge error log:', error.message))
  server.listen(port)

  nitroApp.hooks.hook('close', async () => {
    server.close()
    await stop()
  })
})
