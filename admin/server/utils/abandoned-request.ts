import type { H3Event } from 'h3'

/**
 * A client that leaves before this server answered (#1569): a visitor closing the
 * tab, a proxy or load generator giving up. Nothing else sees it — the response is
 * never written, so neither the error hook nor the proxy's `onResponse` runs — and
 * the work behind it goes on: the proxied API call keeps a database connection
 * busy for nobody, exactly when the pool is saturated.
 *
 * This file is deliberately duplicated between the two apps
 * (`admin/server/utils/abandoned-request.ts`, `web/server/utils/abandoned-request.ts`):
 * a change here is a change on both sides. The copies differ in this header only.
 */

/**
 * Calls `onAbandoned` once when the response is closed before it finished. An event
 * without a Node response (a test double, an in-process call) is never watched.
 */
export function watchForAbandonment(event: H3Event, onAbandoned: () => void): void {
  const res = event.node?.res
  if (!res) return
  res.once('close', () => {
    if (!res.writableFinished) onAbandoned()
  })
}

/**
 * A signal aborted when the client leaves before the response finished: hand it to
 * the upstream call, so the API cancels the request instead of finishing it for
 * nobody (and logs it as `RequestAborted`).
 */
export function abortOnAbandonment(event: H3Event): AbortSignal {
  const controller = new AbortController()
  watchForAbandonment(event, () => controller.abort())
  return controller.signal
}

/**
 * Whether an abandoned request is worth a row: pages and API calls are, the build's
 * bundles and images are not. Those are fetched by the dozen per page, their hashed
 * paths would each become a route of their own, and a visitor who leaves a page
 * abandons them all at once.
 */
export function isReportableAbandonment(path: string | undefined): boolean {
  return Boolean(path) && !path!.startsWith('/_')
}
