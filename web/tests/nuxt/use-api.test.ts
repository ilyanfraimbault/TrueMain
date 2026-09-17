import { flushPromises } from '@vue/test-utils'
import { mockNuxtImport, mountSuspended } from '@nuxt/test-utils/runtime'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { describeFetchError, fetchErrorStatus } from '~/utils/errors'

// `useRequestFetch()` is what forwards the visitor's `X-Forwarded-For` when a call runs
// during SSR (#1557) — on the server Nuxt hands back a fetcher bound to the incoming
// request, in the browser the plain `$fetch`. The rule these tests pin is therefore
// "every backend call goes through it": swapping it for a spy makes a call that
// bypasses it (a bare `$fetch('/api/…')`, the #1557 bug) fail the assertion rather
// than reach the real proxy.
const { requestFetch } = vi.hoisted(() => ({ requestFetch: vi.fn() }))
mockNuxtImport('useRequestFetch', () => () => requestFetch)

function httpError(statusCode: number, statusMessage: string) {
  // The shape ofetch raises: the proxied URL in the message, the status on the error.
  return Object.assign(new Error(`[GET] "/api/champions/266": ${statusCode} ${statusMessage}`), {
    statusCode,
    statusMessage,
    data: { detail: 'Npgsql.PostgresException: 57014 canceling statement' },
  })
}

function probe<T>(run: () => T) {
  let result!: T
  const component = defineComponent({
    setup() {
      result = run()
      return () => h('div')
    },
  })
  return { component, result: () => result }
}

beforeEach(() => {
  requestFetch.mockReset()
})

describe('useApiFetch', () => {
  it('sends a composable handler through the request-bound fetcher, under /api', async () => {
    requestFetch.mockResolvedValue({ championId: 266, position: 'TOP', points: [] })
    const { component } = probe(() => useChampionTrend(266, 'TOP'))

    await mountSuspended(component)
    await flushPromises()

    expect(requestFetch).toHaveBeenCalledTimes(1)
    expect(requestFetch).toHaveBeenCalledWith('/champions/266/trend', {
      baseURL: '/api',
      query: { position: 'TOP' },
    })
  })

  it('keeps the status a handler branches on, so a 404 still means "empty"', async () => {
    requestFetch.mockRejectedValue(httpError(404, 'Not Found'))
    const { component, result } = probe(() => useChampionMatchups(266, 'TOP'))

    await mountSuspended(component)
    await vi.waitFor(() => expect(result().status.value).toBe('success'))

    expect(result().data.value).toBeNull()
    expect(result().error.value).toBeFalsy()
  })
})

describe('useApi', () => {
  it('goes through the same fetcher, and the base cannot be overridden', async () => {
    requestFetch.mockResolvedValue({ totalGames: 1 })
    const { component, result } = probe(() =>
      useApi('/champions/overview', { key: 'use-api-base', baseURL: 'https://elsewhere.example' }))

    await mountSuspended(component)
    await vi.waitFor(() => expect(result().status.value).toBe('success'))

    expect(requestFetch).toHaveBeenCalledTimes(1)
    expect(requestFetch.mock.calls[0]![0]).toBe('/champions/overview')
    expect(requestFetch.mock.calls[0]![1]).toMatchObject({ baseURL: '/api' })
    expect(result().data.value).toEqual({ totalGames: 1 })
  })

  it('surfaces an HTTP failure as its status and the app copy, never the URL or the body', async () => {
    requestFetch.mockRejectedValue(httpError(500, 'Internal Server Error'))
    const { component, result } = probe(() =>
      useApi('/champions/overview', { key: 'use-api-error' }))

    await mountSuspended(component)
    await vi.waitFor(() => expect(result().status.value).toBe('error'))

    const error = result().error.value!
    expect(fetchErrorStatus(error)).toBe(500)
    expect(error.message).toBe(describeFetchError(httpError(500, 'Internal Server Error')))
    expect(error.message).not.toContain('/api')
    expect(JSON.stringify(error)).not.toContain('Npgsql')
  })
})

describe('normalizeApiError', () => {
  it('leaves a failure without a status alone, so an aborted request stays an abort', () => {
    const abort = new DOMException('AsyncData request cancelled by deduplication', 'AbortError')
    expect(normalizeApiError(abort)).toBe(abort)
  })
})
