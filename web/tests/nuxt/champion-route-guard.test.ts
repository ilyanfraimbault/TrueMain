import { mockNuxtImport } from '@nuxt/test-utils/runtime'
import { afterEach, describe, expect, it, vi } from 'vitest'
import type { RouteLocationNormalized } from 'vue-router'
import { championRouteGuard } from '~/utils/champion-route'

// A legacy or mis-cased champion URL is 301'd to the canonical one, under its scope (#1124).
const { navigate } = vi.hoisted(() => ({ navigate: vi.fn() }))
mockNuxtImport('navigateTo', () => navigate)
mockNuxtImport('useChampionSlugs', () => () => ({
  routeAction: () => ({ type: 'redirect', championId: 103, segment: 'ahri' }),
  pathFor: (championId: number) => `/champions/${championId === 103 ? 'ahri' : championId}`,
  truemainPathFor: (nameTag: string, championId: number) => `/truemains/${nameTag}/champions/${championId === 103 ? 'ahri' : championId}`,
}))

afterEach(() => navigate.mockReset())

const route = (params: Record<string, string>) =>
  ({ params, query: { patch: '16.22' }, hash: '' }) as unknown as RouteLocationNormalized

describe('championRouteGuard', () => {
  it('redirects a global champion URL to its canonical path, query kept', () => {
    championRouteGuard(route({ slug: '103' }), 'global')

    expect(navigate).toHaveBeenCalledWith(
      { path: '/champions/ahri', query: { patch: '16.22' }, hash: '' },
      { redirectCode: 301, replace: true },
    )
  })

  it('keeps a player-scoped URL under its player', () => {
    championRouteGuard(route({ slug: '103', nameTag: 'Faker-KR1' }), 'player')

    expect(navigate).toHaveBeenCalledWith(
      expect.objectContaining({ path: '/truemains/Faker-KR1/champions/ahri' }),
      expect.anything(),
    )
  })
})
