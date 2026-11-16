import { defineEventHandler } from 'h3'

/**
 * Authenticated proxy to the backend ops API. The browser only ever talks to
 * `/api/ops/*` on this app; the secret `X-Ops-Key` is injected server-side so it
 * never reaches the client, and every request needs an operator session (set by
 * `/api/auth/login`). The forwarding itself is the site's (`proxyToBackend`).
 */
export default defineEventHandler(async (event) => {
  await requireUserSession(event)
  const { opsApiBaseUrl, opsKey } = useRuntimeConfig(event)
  return proxyToBackend(event, {
    mount: '/api/ops',
    baseUrl: `${opsApiBaseUrl}/ops`,
    headers: { 'X-Ops-Key': opsKey },
    logCategory: 'ops-proxy',
  })
})
