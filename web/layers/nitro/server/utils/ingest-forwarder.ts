import { hostname } from 'node:os'
import { createLogForwarder, LOG_FORWARD_INTERVAL_MS, type ForwardedBatch, type LogForwarder } from './log-forwarder'

export interface IngestForwarder {
  forwarder: LogForwarder
  /** Stops the flush timer and sends what is still pending. */
  stop: () => Promise<void>
}

/**
 * A forwarder posting to the API's `POST /internal/logs` every
 * `LOG_FORWARD_INTERVAL_MS`, under `process`: this server's own errors (the
 * `log-forwarding` plugin), or the edge proxy's failures the admin relays (#1580).
 */
export function startIngestForwarder(options: {
  process: ForwardedBatch['process']
  apiBaseUrl: string
  logIngestKey: string
}): IngestForwarder {
  const forwarder = createLogForwarder({
    process: options.process,
    host: hostname(),
    send: batch => $fetch(`${options.apiBaseUrl}/internal/logs`, {
      method: 'POST',
      body: batch,
      headers: { 'X-Log-Ingest-Key': options.logIngestKey },
      timeout: 3_000,
      retry: 0,
    }),
    onSendError: error => console.error(`[log-forwarder] could not forward ${options.process} errors to the API:`, error instanceof Error ? error.message : error),
  })

  const timer = setInterval(() => {
    void forwarder.flush()
  }, LOG_FORWARD_INTERVAL_MS)
  timer.unref?.()

  return {
    forwarder,
    stop: async () => {
      clearInterval(timer)
      await forwarder.flush()
    },
  }
}
