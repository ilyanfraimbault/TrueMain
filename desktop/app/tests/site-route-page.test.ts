import { mount } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import SiteRoutePage from '../app/pages/[...siteRoute].vue'

/**
 * A route only the site has lands on this page outside Nuxt: it opens on the
 * site and cancels the navigation, so the app stays where it was.
 */
type Middleware = (to: { fullPath: string }) => unknown

const openOnSite = vi.fn(async () => {})
let middleware: Middleware | undefined

beforeEach(() => {
  openOnSite.mockClear()
  middleware = undefined
  vi.stubGlobal('openOnSite', openOnSite)
  vi.stubGlobal('defineNuxtRouteMiddleware', (handler: Middleware) => handler)
  vi.stubGlobal('definePageMeta', (meta: { middleware: Middleware }) => {
    middleware = meta.middleware
  })
})

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('site route page', () => {
  it('opens the whole path on the site and cancels the navigation', () => {
    mount(SiteRoutePage)

    expect(middleware?.({ fullPath: '/players/faker?tab=clips' })).toBe(false)
    expect(openOnSite).toHaveBeenCalledExactlyOnceWith('/players/faker?tab=clips')
  })
})
