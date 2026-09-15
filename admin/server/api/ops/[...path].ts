import { createError, defineEventHandler, proxyRequest } from 'h3'

/**
 * Authenticated proxy to the backend ops API. The browser only ever talks to
 * `/api/ops/*` on this app; the secret `X-Ops-Key` is injected server-side so
 * it never reaches the client. Every request requires a valid operator
 * session (set by `/api/auth/login`).
 *
 * Path handling mirrors `web/server/api/[...path].ts`: the configured base URL
 * is validated up front and the incoming path is rejected if it could escape
 * the backend (`..` traversal, protocol-relative `//host`, or an absolute URL).
 * Both apps share the same guard implementation, copied file-for-file — see
 * `server/utils/proxy-path.ts`.
 */
export default defineEventHandler(async (event) => {
  await requireUserSession(event)

  const { opsApiBaseUrl, opsKey } = useRuntimeConfig(event)

  let base: URL
  try {
    base = new URL(opsApiBaseUrl)
  }
  catch {
    throw createError({ statusCode: 500, statusMessage: 'opsApiBaseUrl misconfigured' })
  }
  if (base.protocol !== 'http:' && base.protocol !== 'https:') {
    throw createError({ statusCode: 500, statusMessage: 'opsApiBaseUrl must be http(s)' })
  }

  // Reject paths that could escape the configured backend. `isUnsafeProxyPath`
  // (server/utils/proxy-path.ts) spells out what "escape" covers and why the raw
  // path alone is not enough to judge it.
  const path = event.path.replace(/^\/api\/ops/, '')
  if (isUnsafeProxyPath(path)) {
    throw createError({ statusCode: 400, statusMessage: 'Invalid request path' })
  }

  return proxyRequest(event, `${opsApiBaseUrl}/ops${path}`, {
    headers: {
      'X-Ops-Key': opsKey,
    },
    // A failing ops call is an admin-tier error worth a row in the Logs page
    // (#1556), counted per route.
    onResponse: (proxied, response) => {
      if (response.status !== 429 && response.status < 500) return
      const route = toRouteTemplate(`/ops${path}`)
      reportToOpsLogs({
        level: response.status >= 500 ? 'Error' : 'Warning',
        category: 'ops-proxy',
        eventType: 'FrontendUpstreamErrors',
        message: `The API answered ${response.status} to ${proxied.method} ${route}`,
        requestMethod: proxied.method,
        requestPath: route,
        statusCode: response.status,
      })
    },
  })
})
