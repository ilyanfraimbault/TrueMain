import { createError, defineEventHandler, getQuery, getRequestURL, proxyRequest } from 'h3'

export default defineEventHandler(async (event) => {
  // Dev-only, opt-in backend mock (`NUXT_DEV_MOCK_API=1`): serve deterministic
  // fixture payloads instead of proxying, so every page can be eyeballed
  // without a running backend. `import.meta.dev` tree-shakes this whole block
  // out of production builds; without the env flag the proxy below still hits
  // the real local backend.
  if (import.meta.dev && devApiMockEnabled()) {
    const pathname = getRequestURL(event).pathname.replace(/^\/api/, '')
    const mock = await resolveDevApiMock(pathname, getQuery(event))
    if (mock !== undefined) return mock
  }

  const { apiBaseUrl } = useRuntimeConfig(event)

  // Validate the configured base URL up front so a misconfigured env var
  // surfaces as a clear 500 instead of letting `proxyRequest` fail downstream
  // on a string like `undefined/champions`.
  let base: URL
  try {
    base = new URL(apiBaseUrl)
  }
  catch {
    throw createError({ statusCode: 500, statusMessage: 'apiBaseUrl misconfigured' })
  }
  if (base.protocol !== 'http:' && base.protocol !== 'https:') {
    throw createError({ statusCode: 500, statusMessage: 'apiBaseUrl must be http(s)' })
  }

  // Reject paths that could escape the configured backend:
  //   `..` segments  → could walk above `base.pathname`
  //   `//host/…`     → protocol-relative, points at a different host
  //   `scheme://…`   → absolute URL, same problem
  const path = event.path.replace(/^\/api/, '')
  const isUnsafe
    = /(^|\/)\.\.(\/|$)/.test(path)
      || /^\/\//.test(path)
      || /^\/?[a-z][a-z0-9+.-]*:\/\//i.test(path)
  if (isUnsafe) {
    throw createError({ statusCode: 400, statusMessage: 'Invalid request path' })
  }

  return proxyRequest(event, `${apiBaseUrl}${path}`)
})
