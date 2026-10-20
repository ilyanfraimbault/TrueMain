import { mountSuspended } from '@nuxt/test-utils/runtime'
import { afterEach, describe, expect, it } from 'vitest'
import { defineComponent, h, nextTick } from 'vue'
import { FavoritesPickRow, FavoriteToggle, PageFavorites, UApp } from '#components'
import type { ChampionStaticListItem } from '#shared/types/static-data'
import { FAVORITE_PICKS_STORAGE_KEY } from '#common/utils/favorites'
import type { FavoritePick } from '#common/utils/favorites'

// Starred champions and matchups (#2080): the star on their pages, their rows
// on the favorites page, grouped by kind.

const icon = (name: string) => `https://ddragon.leagueoflegends.com/cdn/16.20.1/img/champion/${name}.png`
const championsById = new Map<number, ChampionStaticListItem>([
  [103, { championId: 103, name: 'Ahri', iconUrl: icon('Ahri') }],
  [238, { championId: 238, name: 'Zed', iconUrl: icon('Zed') }],
])

const ahriMid: FavoritePick = { kind: 'champion', championId: 103, position: 'MIDDLE', addedAt: 2 }
const ahriVsZed: FavoritePick = { kind: 'matchup', championId: 103, opponentChampionId: 238, position: 'MIDDLE', addedAt: 1 }

afterEach(() => {
  window.localStorage.clear()
  useState('favorite-picks').value = []
})

function inApp(component: object, props: Record<string, unknown>, slots?: Record<string, () => unknown>) {
  return mountSuspended(defineComponent({ setup: () => () => h(UApp, null, () => h(component, props, slots)) }))
}

describe('FavoriteToggle with a pick', () => {
  it('stars the page and unstars it, under the picks key', async () => {
    const wrapper = await mountSuspended(FavoriteToggle, {
      props: { pick: { kind: 'champion', championId: 103, position: 'MIDDLE' }, pickLabel: 'Ahri Middle' },
    })
    const button = wrapper.get('button')
    expect(button.attributes('aria-label')).toBe('Add Ahri Middle to favorites')

    await button.trigger('click')
    expect(button.attributes('aria-pressed')).toBe('true')
    expect(button.attributes('aria-label')).toBe('Remove Ahri Middle from favorites')
    expect(JSON.parse(window.localStorage.getItem(FAVORITE_PICKS_STORAGE_KEY)!)).toMatchObject([
      { kind: 'champion', championId: 103, position: 'MIDDLE' },
    ])

    await button.trigger('click')
    expect(button.attributes('aria-pressed')).toBe('false')
  })
})

describe('FavoritesPickRow', () => {
  it('opens a champion on the position it was starred on', async () => {
    const wrapper = await mountSuspended(FavoritesPickRow, { props: { pick: ahriMid, championsById } })
    expect(wrapper.text()).toContain('Ahri')
    expect(wrapper.text()).toContain('Middle')
    expect(wrapper.get('a').attributes('href')).toMatch(/^\/champions\/[^?]+\?position=MIDDLE$/)
  })

  it('opens a matchup on its champion, opponent and position', async () => {
    const wrapper = await mountSuspended(FavoritesPickRow, { props: { pick: ahriVsZed, championsById } })
    expect(wrapper.text()).toContain('Ahri vs Zed')
    expect(wrapper.get('a').attributes('href')).toBe('/matchup?champion=103&position=MIDDLE&opponent=238')
  })
})

describe('PageFavorites', () => {
  it('groups the starred champions and matchups under their own headings', async () => {
    window.localStorage.setItem(FAVORITE_PICKS_STORAGE_KEY, JSON.stringify([ahriMid, ahriVsZed]))
    useState('favorite-picks:hydrated').value = false
    const wrapper = await inApp(PageFavorites, {})
    await nextTick()

    const headings = wrapper.findAll('h2').map(heading => heading.text())
    expect(headings).toEqual(['Champions', 'Matchups'])
    expect(wrapper.text()).toContain('2 saved')
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
