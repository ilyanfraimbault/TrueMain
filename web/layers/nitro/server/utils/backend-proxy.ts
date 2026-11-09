import type { H3Event } from 'h3'
import { createError, proxyRequest } from 'h3'
import { abortOnAbandonment } from './abandoned-request'
import { isInternalApiPath } from './internal-api-path'
import { reportToOpsLogs, toRouteTemplate } from './log-forwarder'
import { isUnsafeProxyPath } from './proxy-path'

export interface BackendProxyOptions {
  /** Where the app mounts its catch-all proxy (`/api`, `/api/ops`): stripped from the path asked. */
  mount: string
  /** The backend the rest of the path is appended to, as the app's runtime config names it. */
  baseUrl: string
  headers?: Record<string, string>
  /** The ops-log category a failing answer is reported under. */
  logCategory: string
}

/**
 * Forward a request to the API — the site's `/api` and the admin's `/api/ops` (#1625).
 *
 * - The path is read from the URL as the browser sent it: h3 decodes `event.path`, which
 *   turns the `%23` of an encoded `Name#TAG` into a `#` the forwarded request drops as a
 *   fragment (#2290).
 * - A path that could leave the backend is a 400 (`isUnsafeProxyPath`), one that lands on
 *   the API's `/internal` surface a 404: that surface is for server-side callers (#1556).
 * - A 429 or 5xx answered by the API is reported to the ops logs, per API route (#1556).
 * - The call is cancelled when the browser leaves, and that is not a failure (#1569).
 */
export async function proxyToBackend(event: H3Event, options: BackendProxyOptions) {
  let base: URL
  try {
    base = new URL(options.baseUrl)
  }
  catch {
    throw createError({ statusCode: 500, statusMessage: 'Backend base URL misconfigured' })
  }
  if (base.protocol !== 'http:' && base.protocol !== 'https:') {
    throw createError({ statusCode: 500, statusMessage: 'Backend base URL must be http(s)' })
  }

  const asked = event.node?.req?.originalUrl ?? event.path
  const path = asked.startsWith(options.mount) ? asked.slice(options.mount.length) : asked
  if (isUnsafeProxyPath(path)) {
    throw createError({ statusCode: 400, statusMessage: 'Invalid request path' })
  }
  const upstreamPath = `${base.pathname.replace(/\/$/, '')}${path}`
  if (isInternalApiPath(upstreamPath)) {
    throw createError({ statusCode: 404, statusMessage: 'Not Found' })
  }

  const signal = abortOnAbandonment(event)
  try {
    return await proxyRequest(event, `${options.baseUrl}${path}`, {
      fetchOptions: { signal },
      headers: options.headers,
      onResponse: (proxied, response) => {
        if (response.status !== 429 && response.status < 500) return
        const route = toRouteTemplate(upstreamPath)
        reportToOpsLogs({
          level: response.status >= 500 ? 'Error' : 'Warning',
          category: options.logCategory,
          eventType: 'FrontendUpstreamErrors',
          message: `The API answered ${response.status} to ${proxied.method} ${route}`,
          requestMethod: proxied.method,
          requestPath: route,
          statusCode: response.status,
        })
      },
    })
  }
  catch (error) {
    if (signal.aborted) return null
    throw error
  }
}
