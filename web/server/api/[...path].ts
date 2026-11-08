import { createError, defineEventHandler, getQuery, getRequestURL, readBody } from 'h3'

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
    const query = getQuery(event)
    if (devApiMockForcedFailure(pathname, query, body)) {
      throw createError({ statusCode: 500, statusMessage: 'Forced failure (NUXT_DEV_MOCK_FAIL)' })
    }
    const mock = await resolveDevApiMock(pathname, query, body)
    if (mock !== undefined) return mock
  }

  const { apiBaseUrl } = useRuntimeConfig(event)
  return proxyToBackend(event, { mount: '/api', baseUrl: apiBaseUrl, logCategory: 'api-proxy' })
})
