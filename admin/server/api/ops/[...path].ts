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

  // Reject paths that could escape the configured backend:
  //   `..` segments  → could walk above `base.pathname`
  //   `//host/…`     → protocol-relative, points at a different host
  //   `scheme://…`   → absolute URL, same problem
  const path = event.path.replace(/^\/api\/ops/, '')
  const isUnsafe
    = /(^|\/)\.\.(\/|$)/.test(path)
      || /^\/\//.test(path)
      || /^\/?[a-z][a-z0-9+.-]*:\/\//i.test(path)
  if (isUnsafe) {
    throw createError({ statusCode: 400, statusMessage: 'Invalid request path' })
  }

  return proxyRequest(event, `${opsApiBaseUrl}/ops${path}`, {
    headers: {
      'X-Ops-Key': opsKey,
    },
  })
})
