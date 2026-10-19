import { describe, expect, it, vi } from 'vitest'

vi.stubGlobal('useRuntimeConfig', () => ({ opsApiBaseUrl: 'http://api:8080' }))

describe('logForwardingTarget', () => {
  it('files the admin server errors under Admin, posted to the ops API base', async () => {
    const { logForwardingTarget } = await import('~~/server/utils/log-forwarding-target')

    expect(logForwardingTarget()).toEqual({ process: 'Admin', apiBaseUrl: 'http://api:8080' })
  })
})
