import { describe, expect, it, vi } from 'vitest'

vi.stubGlobal('useRuntimeConfig', () => ({ apiBaseUrl: 'http://api:8080' }))

describe('logForwardingTarget', () => {
  it('files the site server errors under Web, posted to the API base', async () => {
    const { logForwardingTarget } = await import('~~/server/utils/log-forwarding-target')

    expect(logForwardingTarget()).toEqual({ process: 'Web', apiBaseUrl: 'http://api:8080' })
  })
})
