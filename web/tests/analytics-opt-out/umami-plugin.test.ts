import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest'

/**
 * The Umami plugin (#728, #2089, #2149): it loads the page-view tracker only,
 * never the session replay recorder, and not at all for a visitor who opted
 * out. Every send carries the anonymous visitor id.
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
    expect(window.localStorage.getItem('truemain.visitor')).toBeNull()
  })

  it('stamps every send with the stored visitor id', () => {
    umami.host = 'https://analytics.example.test'
    umami.websiteId = 'site-id'

    plugin()
    plugin()

    const [first, second] = useScriptUmamiAnalytics.mock.calls.map(([options]) => options.beforeSend)
    const { id } = JSON.parse(window.localStorage.getItem('truemain.visitor')!)
    expect(first('event', { website: 'site-id', url: '/' })).toEqual({ website: 'site-id', url: '/', id })
    expect(second('event', {}).id).toBe(id)
  })

  it('sends no id when the device cannot keep one', () => {
    umami.host = 'https://analytics.example.test'
    umami.websiteId = 'site-id'
    const localStorage = vi.spyOn(window, 'localStorage', 'get').mockReturnValue({
      getItem: () => null,
      setItem: () => {
        throw new Error('blocked')
      },
    } as unknown as Storage)

    plugin()

    localStorage.mockRestore()
    expect(useScriptUmamiAnalytics).toHaveBeenCalledOnce()
    expect(useScriptUmamiAnalytics.mock.calls[0]![0].beforeSend).toBeUndefined()
  })

  it('removes the visitor id of a visitor who opted out', () => {
    umami.host = 'https://analytics.example.test'
    umami.websiteId = 'site-id'
    window.localStorage.setItem('truemain.visitor', JSON.stringify({ id: 'x', createdAt: Date.now() }))
    window.localStorage.setItem('umami.disabled', '1')

    plugin()

    expect(window.localStorage.getItem('truemain.visitor')).toBeNull()
  })
})
