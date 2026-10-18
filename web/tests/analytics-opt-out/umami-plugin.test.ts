import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest'

/**
 * The Umami plugin (#728, #2089): it loads the page-view tracker only, never
 * the session replay recorder, and not at all for a visitor who opted out.
 * The plugin relies on Nuxt auto-imports, stubbed as globals here.
 */
const umami = { host: '', websiteId: '' }
const useScriptUmamiAnalytics = vi.fn()

let plugin: () => void

beforeAll(async () => {
  vi.stubGlobal('defineNuxtPlugin', (setup: () => void) => setup)
  vi.stubGlobal('useRuntimeConfig', () => ({ public: { umami } }))
  vi.stubGlobal('useScriptUmamiAnalytics', useScriptUmamiAnalytics)
  plugin = (await import('~/plugins/umami.client')).default as unknown as () => void
})

afterEach(() => {
  useScriptUmamiAnalytics.mockReset()
  window.localStorage.clear()
  umami.host = ''
  umami.websiteId = ''
})

describe('umami plugin', () => {
  it('loads only the tracker from the configured instance', () => {
    umami.host = 'https://analytics.example.test/'
    umami.websiteId = 'site-id'

    plugin()

    expect(useScriptUmamiAnalytics).toHaveBeenCalledOnce()
    const options = useScriptUmamiAnalytics.mock.calls[0]![0]
    expect(options.websiteId).toBe('site-id')
    expect(options.scriptInput.src).toBe('https://analytics.example.test/script.js')
    expect(JSON.stringify(options)).not.toContain('recorder')
  })

  it('loads nothing when the instance is not configured', () => {
    plugin()

    expect(useScriptUmamiAnalytics).not.toHaveBeenCalled()
  })

  it('loads nothing for a visitor who opted out', () => {
    umami.host = 'https://analytics.example.test'
    umami.websiteId = 'site-id'
    window.localStorage.setItem('umami.disabled', '1')

    plugin()

    expect(useScriptUmamiAnalytics).not.toHaveBeenCalled()
  })
})
