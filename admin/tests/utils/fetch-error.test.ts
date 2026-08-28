import { describe, expect, it } from 'vitest'
import { extractFetchError, extractFetchErrorTraceId } from '~/utils/fetch-error'

// Every panel funnels its failures through `extractFetchError`, so the order of the
// cascade is what decides whether an operator reads the backend's explanation or a bare
// "Not Found". The rungs are asserted one at a time, each with the rungs *below* it also
// populated: an equality that only holds because nothing else was set proves nothing
// about precedence.
describe('extractFetchError', () => {
  it('prefers the ProblemDetails detail over everything else', () => {
    expect(extractFetchError({
      data: {
        detail: 'No aggregate exists for patch 16.4',
        title: 'Not Found',
        message: 'local',
        statusMessage: 'local status',
      },
      statusMessage: 'Not Found',
      message: 'HTTP 404',
    })).toBe('No aggregate exists for patch 16.4')
  })

  it('falls back to the ProblemDetails title for a bare NotFound() with no detail', () => {
    // `return NotFound()` produces a title and no detail; the title still names the
    // failure better than ofetch's generic status text does.
    expect(extractFetchError({
      data: { title: 'Not Found' },
      statusMessage: 'Internal Server Error',
      message: 'HTTP 404',
    })).toBe('Not Found')
  })

  it('reads a local createError body, which never goes through ProblemDetails', () => {
    expect(extractFetchError({
      data: { message: 'Invalid credentials' },
      statusMessage: 'Unauthorized',
    })).toBe('Invalid credentials')
  })

  it('reads a local createError statusMessage when there is no message', () => {
    expect(extractFetchError({
      data: { statusMessage: 'Session expired' },
      statusMessage: 'Unauthorized',
      message: 'HTTP 401',
    })).toBe('Session expired')
  })

  it("uses ofetch's own status text only once no body said anything", () => {
    expect(extractFetchError({ statusMessage: 'Bad Gateway', message: 'HTTP 502' }))
      .toBe('Bad Gateway')
  })

  it('uses the error message last, for a failure that never reached the backend', () => {
    expect(extractFetchError(new TypeError('fetch failed'))).toBe('fetch failed')
  })

  it('falls back for shapes that carry no message at all', () => {
    expect(extractFetchError(null)).toBe('Unexpected error')
    expect(extractFetchError(undefined)).toBe('Unexpected error')
    expect(extractFetchError({})).toBe('Unexpected error')
    expect(extractFetchError({ data: {} })).toBe('Unexpected error')
    expect(extractFetchError('a bare string')).toBe('Unexpected error')
  })

  it('honours a caller-supplied fallback so the panel can name what failed', () => {
    expect(extractFetchError({}, 'Could not load crashes')).toBe('Could not load crashes')
  })
})

describe('extractFetchErrorTraceId', () => {
  it('surfaces the backend traceId so a reported error can be matched to server logs', () => {
    expect(extractFetchErrorTraceId({ data: { traceId: '00-abc-def-01' } })).toBe('00-abc-def-01')
  })

  it('is undefined for an error that never reached the backend', () => {
    // A network drop or a local createError has no traceId; the panel must render no
    // trace line rather than an empty one.
    expect(extractFetchErrorTraceId(new TypeError('fetch failed'))).toBeUndefined()
    expect(extractFetchErrorTraceId({ data: { detail: 'no trace here' } })).toBeUndefined()
    expect(extractFetchErrorTraceId(null)).toBeUndefined()
  })
})
