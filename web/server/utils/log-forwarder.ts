/**
 * Ships this server's own errors to the ops logs (#1556), through the API's
 * `POST /internal/logs`, so a failure of the public site's server shows up in the
 * admin Logs page next to the API's instead of only in container output.
 *
 * This file is deliberately duplicated between the two apps
 * (`web/server/utils/log-forwarder.ts`, `admin/server/utils/log-forwarder.ts`),
 * like `proxy-path.ts`: **a change here is a change on both sides**, and each app
 * pins the behaviour with its own test. The copies differ in this header only.
 *
 * Built so that reporting can never make an incident worse:
 * - identical errors are folded into one entry with a count, so a burst of the
 *   same failure costs one row per flush, not one per request;
 * - distinct errors are bounded between flushes, and the excess is counted in a
 *   single warning rather than kept;
 * - a failed send is dropped, never retried, and never throws — the API being
 *   unreachable is exactly when this runs, and a retry loop would pile onto it.
 */

export type ForwardedLevel = 'Warning' | 'Error' | 'Critical'

export type ForwardedEventType = 'FrontendServerError' | 'FrontendUpstreamErrors' | 'FrontendRequestAborted'

export interface ForwardedError {
  level: ForwardedLevel
  category: string
  message: string
  exception?: string | null
  eventType?: ForwardedEventType | null
  requestMethod?: string | null
  /** A route template (see `toRouteTemplate`), never a raw path. */
  requestPath?: string | null
  statusCode?: number | null
  durationMs?: number | null
}

export interface ForwardedEntry {
  level: ForwardedLevel
  category: string
  message: string
  exception: string | null
  eventType: ForwardedEventType | null
  requestMethod: string | null
  requestPath: string | null
  statusCode: number | null
  durationMs: number | null
  timestampUtc: string
  count: number
}

export interface ForwardedBatch {
  process: 'Web' | 'Admin'
  host: string
  entries: ForwardedEntry[]
}

export interface LogForwarderOptions {
  process: 'Web' | 'Admin'
  host: string
  send: (batch: ForwardedBatch) => Promise<unknown>
  /** Distinct errors held between two flushes; the excess is counted, not kept. */
  maxPending?: number
  /** Entries per request. The API refuses more than 50. */
  maxBatch?: number
  now?: () => Date
  onSendError?: (error: unknown) => void
}

export interface LogForwarder {
  report: (error: ForwardedError) => void
  flush: () => Promise<void>
  readonly pendingCount: number
}

/** How often the plugin flushes what was reported. */
export const LOG_FORWARD_INTERVAL_MS = 5_000

// The API's own limits on an entry; truncating here keeps one oversized stack
// trace from getting the whole batch rejected.
const CATEGORY_LIMIT = 128
const MESSAGE_LIMIT = 4_000
const EXCEPTION_LIMIT = 16_000
const PATH_LIMIT = 512
const API_BATCH_LIMIT = 50

function truncate(value: string, limit: number): string {
  return value.length > limit ? value.slice(0, limit) : value
}

export function createLogForwarder(options: LogForwarderOptions): LogForwarder {
  const maxPending = options.maxPending ?? 200
  const maxBatch = Math.min(options.maxBatch ?? API_BATCH_LIMIT, API_BATCH_LIMIT)
  const now = options.now ?? (() => new Date())
  const pending = new Map<string, ForwardedEntry>()
  let overflow = 0

  function report(error: ForwardedError): void {
    const entry: ForwardedEntry = {
      level: error.level,
      category: truncate(error.category, CATEGORY_LIMIT),
      message: truncate(error.message || 'Unknown error', MESSAGE_LIMIT),
      exception: error.exception ? truncate(error.exception, EXCEPTION_LIMIT) : null,
      eventType: error.eventType ?? null,
      requestMethod: error.requestMethod ?? null,
      requestPath: error.requestPath ? truncate(error.requestPath, PATH_LIMIT) : null,
      statusCode: error.statusCode ?? null,
      durationMs: error.durationMs ?? null,
      timestampUtc: now().toISOString(),
      count: 1,
    }
    const key = JSON.stringify([
      entry.level,
      entry.eventType,
      entry.category,
      entry.statusCode,
      entry.requestMethod,
      entry.requestPath,
      entry.message,
    ])
    const existing = pending.get(key)
    if (existing) {
      existing.count++
      return
    }
    if (pending.size >= maxPending) {
      overflow++
      return
    }
    pending.set(key, entry)
  }

  async function flush(): Promise<void> {
    const entries = [...pending.values()]
    pending.clear()
    if (overflow > 0) {
      entries.push({
        level: 'Warning',
        category: 'log-forwarder',
        message: `${overflow} error report(s) were not forwarded: more than ${maxPending} distinct errors arrived between two flushes`,
        exception: null,
        eventType: null,
        requestMethod: null,
        requestPath: null,
        statusCode: null,
        durationMs: null,
        timestampUtc: now().toISOString(),
        count: 1,
      })
      overflow = 0
    }
    for (let start = 0; start < entries.length; start += maxBatch) {
      try {
        await options.send({ process: options.process, host: options.host, entries: entries.slice(start, start + maxBatch) })
      }
      catch (error) {
        try {
          options.onSendError?.(error)
        }
        catch {
          // Reporting a reporting failure must not fail either.
        }
      }
    }
  }

  return {
    report,
    flush,
    get pendingCount() {
      return pending.size
    },
  }
}

/**
 * `/api/truemains/Faker-KR1/matches?page=2` → `/api/truemains/{nameTag}/matches`.
 * Numbers and player tags become placeholders and the query is dropped: the
 * template is what identical failures aggregate on, and it keeps player names and
 * query values out of the logs.
 */
export function toRouteTemplate(path: string | null | undefined): string | null {
  if (!path) return null
  const [pathname = ''] = path.split('?')
  const segments = pathname.split('/')
  const template = segments
    .map((segment, index) => {
      if (/^\d+$/.test(segment)) return '{n}'
      const previous = index > 0 ? segments[index - 1] : undefined
      if (previous === 'truemains' && segment && segment !== 'search' && segment !== 'favorites') {
        return '{nameTag}'
      }
      return segment
    })
    .join('/')
  return truncate(template, PATH_LIMIT)
}

/** The HTTP status an error stands for; anything without one is a 500. */
export function errorStatus(error: unknown): number {
  const candidate = error as { statusCode?: unknown, status?: unknown } | null
  const status = candidate?.statusCode ?? candidate?.status
  return typeof status === 'number' ? status : 500
}

let activeForwarder: LogForwarder | null = null

/** Installed by the `log-forwarding` plugin; null while ingestion is off. */
export function setLogForwarder(forwarder: LogForwarder | null): void {
  activeForwarder = forwarder
}

/** Reports an error to the ops logs; a no-op while ingestion is off. */
export function reportToOpsLogs(error: ForwardedError): void {
  activeForwarder?.report(error)
}
