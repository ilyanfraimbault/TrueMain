import { mountSuspended } from '@nuxt/test-utils/runtime'
import { afterEach, describe, expect, it } from 'vitest'
import { defineComponent, h, nextTick } from 'vue'
import { FavoritesChampionRow, FavoriteToggle, PageFavorites, UApp } from '#components'
import type { ChampionStaticListItem } from '#shared/types/static-data'
import { FAVORITE_CHAMPIONS_STORAGE_KEY } from '#common/utils/favorites'
import type { FavoriteChampion } from '#common/utils/favorites'

// Starred champions (#2080): the star on the champion banner, their rows on
// the favorites page.

const icon = (name: string) => `https://ddragon.leagueoflegends.com/cdn/16.20.1/img/champion/${name}.png`
const championsById = new Map<number, ChampionStaticListItem>([
  [103, { championId: 103, name: 'Ahri', iconUrl: icon('Ahri') }],
])

const ahriMid: FavoriteChampion = { championId: 103, position: 'MIDDLE', addedAt: 2 }

afterEach(() => {
  window.localStorage.clear()
  useState('favorite-champions').value = []
})

function inApp(component: object, props: Record<string, unknown>, slots?: Record<string, () => unknown>) {
  return mountSuspended(defineComponent({ setup: () => () => h(UApp, null, () => h(component, props, slots)) }))
}

describe('FavoriteToggle with a champion', () => {
  it('stars the champion and unstars it, under the champions key', async () => {
    const wrapper = await mountSuspended(FavoriteToggle, {
      props: { champion: { championId: 103, position: 'MIDDLE' }, championLabel: 'Ahri Middle' },
    })
    const button = wrapper.get('button')
    expect(button.attributes('aria-label')).toBe('Add Ahri Middle to favorites')

    await button.trigger('click')
    expect(button.attributes('aria-pressed')).toBe('true')
    expect(button.attributes('aria-label')).toBe('Remove Ahri Middle from favorites')
    expect(JSON.parse(window.localStorage.getItem(FAVORITE_CHAMPIONS_STORAGE_KEY)!)).toMatchObject([
      { championId: 103, position: 'MIDDLE' },
    ])

    await button.trigger('click')
    expect(button.attributes('aria-pressed')).toBe('false')
  })
})

describe('FavoritesChampionRow', () => {
  it('shows the position as its icon, not as text, and opens the champion on it', async () => {
    const wrapper = await mountSuspended(FavoritesChampionRow, { props: { favorite: ahriMid, championsById } })
    expect(wrapper.get('a').text()).toBe('Ahri')
    expect(wrapper.find('a [title="Middle"]').exists()).toBe(true)
    expect(wrapper.get('a').attributes('href')).toMatch(/^\/champions\/[^?]+\?position=MIDDLE$/)
  })

  it('draws no position icon for a champion starred on every position', async () => {
    const wrapper = await mountSuspended(FavoritesChampionRow, {
      props: { favorite: { ...ahriMid, position: null }, championsById },
    })
    expect(wrapper.find('a [title]').exists()).toBe(false)
    expect(wrapper.get('a').attributes('href')).not.toContain('position')
  })
})

describe('PageFavorites', () => {
  it('lists the starred champions under their own heading', async () => {
    window.localStorage.setItem(FAVORITE_CHAMPIONS_STORAGE_KEY, JSON.stringify([ahriMid]))
    useState('favorite-champions:hydrated').value = false
    const wrapper = await inApp(PageFavorites, {})
    await nextTick()

    expect(wrapper.findAll('h2').map(heading => heading.text())).toEqual(['Champions'])
    expect(wrapper.text()).toContain('1 saved')
    expect(wrapper.text()).not.toContain('No favorites yet')
  })

  it('keeps the empty state off when only the host has favorites', async () => {
    const wrapper = await inApp(PageFavorites, { extraCount: 1 }, { extra: () => h('p', { class: 'host-extra' }, 'Clips') })
    await nextTick()

    expect(wrapper.text()).not.toContain('No favorites yet')
    expect(wrapper.find('.host-extra').exists()).toBe(true)
  })

  it('says so when there is nothing at all', async () => {
    const wrapper = await inApp(PageFavorites, {})
    await nextTick()
    expect(wrapper.text()).toContain('No favorites yet')
  })
})
