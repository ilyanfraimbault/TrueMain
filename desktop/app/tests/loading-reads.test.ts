import { mount } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { computed } from 'vue'
import GamePlayerIntel from '../app/components/game/GamePlayerIntel.vue'
import LoadingBoard from '../app/components/loading/LoadingBoard.vue'
import type { KnownPlayer, LoadingPlayer, PlayerForm } from '~/types/loading'
import { readValue } from '~/types/loading'

/**
 * The loading screen's reads as the board and the Game page draw them (#2472):
 * each field pending, read or failed, a hidden player with nothing read.
 */

const global = {
  stubs: {
    USkeleton: { template: '<span data-skeleton />' },
    UIcon: true,
    RankIcon: true,
    TruemainMark: true,
    LoadingRecent: true,
    GameLaneEdge: true,
  },
}

const PENDING = { state: 'pending' } as const
const FAILED = { state: 'failed' } as const

function form(overrides: Partial<PlayerForm> = {}): PlayerForm {
  return { games: 20, championGames: 0, championWins: 0, championKills: 0, championDeaths: 0, championAssists: 0, positions: [], streak: 0, recent: [], ...overrides }
}

function known(overrides: Partial<KnownPlayer> = {}): KnownPlayer {
  return { kind: 'known', riotId: 'Me#EUW', championId: 103, team: 'ORDER', position: 'MIDDLE', isMe: true, form: PENDING, rank: PENDING, laning: PENDING, ...overrides }
}

const hidden: LoadingPlayer = { kind: 'hidden', championId: 81, team: 'CHAOS', position: 'MIDDLE', isMe: false }

beforeEach(() => {
  vi.stubGlobal('computed', computed)
  vi.stubGlobal('useChampionStatics', () => ({ nameOf: () => 'Ahri', portraitOf: () => null }))
  vi.stubGlobal('useTruemainMarkOf', () => () => null)
})

afterEach(() => vi.unstubAllGlobals())

function intel(line: LoadingPlayer | null, reading = true) {
  return mount(GamePlayerIntel, { props: { riotId: 'Me#EUW', position: 'MIDDLE', championId: 103, line, reading }, global })
}

describe('GamePlayerIntel', () => {
  it('draws a line still to come as pending while the board reads, and nothing once it is done', () => {
    expect(intel(null).findAll('[data-skeleton]').length).toBeGreaterThan(0)
    const done = intel(null, false)
    expect(done.findAll('[data-skeleton]')).toHaveLength(0)
    expect(done.text()).not.toContain('History unavailable')
  })

  it('says a failed history is unavailable and an unranked standing is unranked', () => {
    const wrapper = intel(known({ form: FAILED, rank: { state: 'ok', value: null } }), false)
    expect(wrapper.text()).toContain('History unavailable')
    expect(wrapper.text()).toContain('Unranked')
    expect(wrapper.findAll('[data-skeleton]')).toHaveLength(0)
  })

  it('keeps a pending standing pending beside a read history', () => {
    const wrapper = intel(known({ form: { state: 'ok', value: form() } }))
    expect(wrapper.text()).toContain('First time')
    expect(wrapper.findAll('[data-skeleton]')).toHaveLength(1)
  })

  it('draws a hidden player as anonymous with nothing pending', () => {
    const wrapper = intel(hidden)
    expect(wrapper.text()).toContain('Anonymous')
    expect(wrapper.findAll('[data-skeleton]')).toHaveLength(0)
  })
})

describe('LoadingBoard', () => {
  it('draws each player by what was read of them', () => {
    const players: LoadingPlayer[] = [
      known({ riotId: 'Read#EUW', position: 'TOP', form: { state: 'ok', value: form() } }),
      known({ riotId: 'Failed#EUW', position: 'JUNGLE', isMe: false, form: FAILED }),
      known({ riotId: 'Pending#EUW', position: 'MIDDLE', isMe: false }),
      hidden,
    ]
    const wrapper = mount(LoadingBoard, { props: { players }, global })
    const text = wrapper.text()
    expect(text).toContain('1st')
    expect(text).toContain('–')
    expect(text).toContain('Anonymous')
    expect(wrapper.findAll('[data-skeleton]')).toHaveLength(1)
  })
})

describe('readValue', () => {
  it('is the value once read, else null', () => {
    expect(readValue({ state: 'ok', value: 3 })).toBe(3)
    expect(readValue(PENDING)).toBeNull()
    expect(readValue(FAILED)).toBeNull()
  })
})
