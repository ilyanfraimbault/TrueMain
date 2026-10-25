import { createServer, type Server } from 'node:net'
import type { ForwardedError } from '../../../web/layers/nitro/server/utils/log-forwarder'

/**
 * The edge proxy's own failures (#1580): requests it answered with a 5xx because it
 * could not reach or read the site's or the admin's server — the class no other
 * process ever sees. The edge streams its error log here as JSON lines (Caddy's
 * `output net`, see `docs/ci.md`); each line naming a 5xx becomes one report,
 * which the forwarder folds per site, status and error before it reaches the API.
 */

// `http.log.error.<site>`: the site's logger name, set in the edge's Caddyfile
// (`log web`, `log admin`). Taken from the config, never from the request, so the
// set of sites stays small and nobody can invent one with a Host header.
const ERROR_LOGGER = /^http\.log\.error(?:\.([\w-]{1,32}))?$/
const ERROR_TEXT_LIMIT = 300
const ERROR_INPUT_LIMIT = 2_000

/**
 * `read tcp 172.18.0.4:51234->172.18.0.5:3000: read: connection reset by peer`
 * → `read tcp 172.18.0.5:3000: read: connection reset by peer`. The local end
 * carries an ephemeral port, which would make every occurrence a distinct error.
 */
export function normalizeEdgeError(message: string): string {
  // Token by token rather than a regular expression: the line comes from the network,
  // and a pattern over unbounded runs of non-space characters backtracks quadratically.
  const normalized = message
    .slice(0, ERROR_INPUT_LIMIT)
    .split(' ')
    .map((token) => {
      const arrow = token.lastIndexOf('->')
      return arrow === -1 ? token : token.slice(arrow + 2)
    })
    .filter(token => token !== '')
    .join(' ')
  return (normalized || 'unknown error').slice(0, ERROR_TEXT_LIMIT)
}

/**
 * One line of the edge's error log as a report, or null when it is not a 5xx the
 * edge generated. Only the site, the status, the error text and the duration are
 * kept: the request itself (path, client address, headers) never leaves this
 * function.
 */
export function toEdgeProxyError(line: string): ForwardedError | null {
  let record: unknown
  try {
    record = JSON.parse(line)
  }
  catch {
    return null
  }
  if (!record || typeof record !== 'object') return null
  const { logger, duration, first_error: firstError } = record as Record<string, unknown>
  // When the error route itself fails (the maintenance page missing, say), Caddy logs
  // that second error and nests the one the visitor's request hit under `first_error`.
  const failure = (firstError && typeof firstError === 'object' ? firstError : record) as Record<string, unknown>
  const { msg, status } = failure
  if (typeof logger !== 'string') return null
  const match = ERROR_LOGGER.exec(logger)
  if (!match) return null
  if (typeof status !== 'number' || !Number.isInteger(status) || status < 500 || status > 599) return null

  const site = match[1] ?? 'default'
  const error = normalizeEdgeError(typeof msg === 'string' ? msg : '')
  return {
    level: 'Error',
    category: 'edge',
    eventType: 'EdgeProxyError',
    message: `The edge proxy could not serve a request to ${site} (${status}): ${error}`,
    statusCode: status,
    durationMs: typeof duration === 'number' && duration >= 0 ? Math.round(duration * 1000) : null,
  }
}

export interface LineServerOptions {
  /** A longer line is dropped whole: an error log line is a few hundred bytes. */
  maxLineLength?: number
  /** Only the edge connects; anything beyond this is refused. */
  maxConnections?: number
}

/**
 * A TCP server calling `onLine` for each newline-terminated line it receives.
 * Never throws at its caller: a broken connection is dropped, a throwing
 * `onLine` is ignored.
 */
export function createLineServer(onLine: (line: string) => void, options: LineServerOptions = {}): Server {
  const maxLineLength = options.maxLineLength ?? 16_384
  const server = createServer((socket) => {
    socket.setEncoding('utf8')
    let buffer = ''
    let discarding = false
    socket.on('data', (chunk: string) => {
      buffer += chunk
      let newline = buffer.indexOf('\n')
      while (newline !== -1) {
        const line = buffer.slice(0, newline)
        buffer = buffer.slice(newline + 1)
        if (discarding) {
          discarding = false
        }
        else if (line.length <= maxLineLength && line.trim()) {
          try {
            onLine(line)
          }
          catch {
            // One unreadable line must not cost the connection.
          }
        }
        newline = buffer.indexOf('\n')
      }
      if (buffer.length > maxLineLength) {
        buffer = ''
        discarding = true
      }
    })
    socket.on('error', () => socket.destroy())
  })
  server.maxConnections = options.maxConnections ?? 4
  return server
}

/** `NUXT_EDGE_LOG_PORT` as a port number, or null when unset or not a port. */
export function edgeLogPort(value: unknown): number | null {
  let text = ''
  if (typeof value === 'number') text = String(value)
  else if (typeof value === 'string') text = value.trim()
  if (!/^\d{1,5}$/.test(text)) return null
  const port = Number(text)
  return port >= 1 && port <= 65_535 ? port : null
}
