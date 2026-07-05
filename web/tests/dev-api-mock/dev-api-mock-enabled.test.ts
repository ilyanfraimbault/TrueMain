import { afterEach, describe, expect, it } from 'vitest'
import { devApiMockEnabled } from '~~/server/utils/dev-api-mock'

/**
 * `devApiMockEnabled` gates the dev-only backend mock. It must treat the flag
 * as an explicit `'1'`/`'true'` allowlist rather than `Boolean(env)`: a bare
 * `Boolean('0')` is truthy, which would make the mock impossible to disable by
 * value (`NUXT_DEV_MOCK_API=0`). These tests freeze that contract.
 */
describe('devApiMockEnabled', () => {
  const original = process.env.NUXT_DEV_MOCK_API

  afterEach(() => {
    if (original === undefined) delete process.env.NUXT_DEV_MOCK_API
    else process.env.NUXT_DEV_MOCK_API = original
  })

  it('enables for the documented "1" opt-in', () => {
    process.env.NUXT_DEV_MOCK_API = '1'
    expect(devApiMockEnabled()).toBe(true)
  })

  it('enables for "true", case-insensitively', () => {
    process.env.NUXT_DEV_MOCK_API = 'TRUE'
    expect(devApiMockEnabled()).toBe(true)
  })

  it('disables when the flag is unset', () => {
    delete process.env.NUXT_DEV_MOCK_API
    expect(devApiMockEnabled()).toBe(false)
  })

  it('disables for falsy-looking opt-outs Boolean() would wrongly enable', () => {
    for (const value of ['0', 'false', '']) {
      process.env.NUXT_DEV_MOCK_API = value
      expect(devApiMockEnabled(), `flag=${JSON.stringify(value)}`).toBe(false)
    }
  })
})
