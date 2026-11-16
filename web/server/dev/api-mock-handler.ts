import { createError, defineEventHandler, getQuery, readBody } from 'h3'
import { devApiMockEnabled, devApiMockForcedFailure, resolveDevApiMock } from './api-mock'

/**
 * The dev backend mock's one mount (`NUXT_DEV_MOCK_API=1`): a middleware on
 * `/api` that `nuxt.config.ts` registers under `$development` only, so neither
 * it nor the fixtures behind it exist in a production build. It answers the
 * routes the mock knows and lets every other request fall through to the proxy
 * (`server/api/[...path].ts`), which then reaches a real local backend.
 */
export default defineEventHandler(async (event) => {
  if (!devApiMockEnabled()) return
  const requested = new URL(event.node.req.originalUrl ?? event.node.req.url ?? '/', 'http://mock')
  const pathname = requested.pathname.replace(/^\/api(?=\/)/, '')
  // POST mocks (composition-build) shape their payload from the body.
  const body = event.method === 'POST' ? await readBody(event).catch(() => undefined) : undefined
  const query = getQuery(event)
  if (devApiMockForcedFailure(pathname, query, body)) {
    throw createError({ statusCode: 500, statusMessage: 'Forced failure (NUXT_DEV_MOCK_FAIL)' })
  }
  const mock = await resolveDevApiMock(pathname, query, body)
  if (mock !== undefined) return mock
})
