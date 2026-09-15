import { createError, defineEventHandler, getQuery, getRequestURL, proxyRequest, readBody } from 'h3'

export default defineEventHandler(async (event) => {
  // Dev-only, opt-in backend mock (`NUXT_DEV_MOCK_API=1`): serve deterministic
  // fixture payloads instead of proxying, so every page can be eyeballed
  // without a running backend. `import.meta.dev` tree-shakes this whole block
  // out of production builds; without the env flag the proxy below still hits
  // the real local backend.
  if (import.meta.dev && devApiMockEnabled()) {
    const pathname = getRequestURL(event).pathname.replace(/^\/api/, '')
    // POST mocks (composition-build) shape their payload from the body.
    const body = event.method === 'POST'
      ? await readBody(event).catch(() => undefined)
      : undefined
    const mock = await resolveDevApiMock(pathname, getQuery(event), body)
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

  // Reject paths that could escape the configured backend. `isUnsafeProxyPath`
  // (server/utils/proxy-path.ts) spells out what "escape" covers and why the raw
  // path alone is not enough to judge it; the admin's ops proxy carries the same
  // guard, and the two copies must be changed together.
  const path = event.path.replace(/^\/api/, '')
  if (isUnsafeProxyPath(path)) {
    throw createError({ statusCode: 400, statusMessage: 'Invalid request path' })
  }
  // The API's `/internal` surface is for server-side callers with their own key
  // (#1556), never for a visitor going through this proxy.
  if (isInternalApiPath(path)) {
    throw createError({ statusCode: 404, statusMessage: 'Not Found' })
  }

  return proxyRequest(event, `${apiBaseUrl}${path}`, {
    // What the visitor was served, counted per route in the ops logs (#1556): a
    // 429 is the API shedding load, a 5xx is the API failing.
    onResponse: (proxied, response) => {
      if (response.status !== 429 && response.status < 500) return
      const route = toRouteTemplate(`/api${path}`)
      reportToOpsLogs({
        level: response.status >= 500 ? 'Error' : 'Warning',
        category: 'api-proxy',
        eventType: 'FrontendUpstreamErrors',
        message: `The API answered ${response.status} to ${proxied.method} ${route}`,
        requestMethod: proxied.method,
        requestPath: route,
        statusCode: response.status,
      })
    },
  })
})
