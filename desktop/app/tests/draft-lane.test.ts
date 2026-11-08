import { mount } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, ref } from 'vue'
import type { DraftState, TeamSlot } from '~/types/lcu'
import { useDraftLane } from '~/composables/useDraftLane'
import { useDraftTeamRows } from '~/composables/useDraftTeamRows'
import { freeLane, holdsEveryLane, withLane } from '~/utils/draft-lane'

/**
 * The lane the draft ranks for when the client assigns none (#2461): blind
 * pick, or a custom game whose bots carry the lane the lobby gave them.
 */

function slot(position: string, isMe = false): TeamSlot {
  return { championId: null, position, locked: false, isMe }
}

/** A custom lobby: four bots on their lanes, the player on none. */
const customTeam = [slot('', true), slot('MIDDLE'), slot('JUNGLE'), slot('BOTTOM'), slot('UTILITY')]

function draft(overrides: Partial<DraftState> = {}): DraftState {
  return {
    myPosition: '',
    myChampion: null,
    myChampionLocked: false,
    allyChampions: [],
    enemyChampions: [],
    myTeam: customTeam,
    allyBans: [],
    enemyBans: [],
    secondsLeft: 30,
    timerPhase: 'BAN_PICK',
    myAction: null,
    myNextAction: null,
    turnsUntilMyAction: null,
    ...overrides,
  }
}

describe('freeLane', () => {
  it('is the one lane the allies leave', () => {
    expect(freeLane(customTeam)).toBe('TOP')
  })

  it('is null while two lanes or more are open', () => {
    expect(freeLane([slot('', true), slot(''), slot(''), slot(''), slot('')])).toBeNull()
    expect(freeLane([slot('', true), slot('MIDDLE'), slot('JUNGLE'), slot('BOTTOM'), slot('')])).toBeNull()
  })

  it('is null outside a five-cell side', () => {
    expect(freeLane([slot('', true), slot('MIDDLE')])).toBeNull()
  })
})

describe('withLane', () => {
  it('sets our position and our own slot, nobody else', () => {
    const laned = withLane(draft(), 'TOP')
    expect(laned.myPosition).toBe('TOP')
    expect(laned.myTeam.map(cell => cell.position)).toEqual(['TOP', 'MIDDLE', 'JUNGLE', 'BOTTOM', 'UTILITY'])
  })

  it('returns the draft untouched when the lane is already ours', () => {
    const ranked = draft({ myPosition: 'MIDDLE' })
    expect(withLane(ranked, 'MIDDLE')).toBe(ranked)
  })
})

describe('holdsEveryLane', () => {
  it('is true once each lane is held once', () => {
    expect(holdsEveryLane(withLane(draft(), 'TOP').myTeam)).toBe(true)
  })

  it('is false with a lane missing or held twice', () => {
    expect(holdsEveryLane(customTeam)).toBe(false)
    expect(holdsEveryLane(withLane(draft(), 'MIDDLE').myTeam)).toBe(false)
  })
})

describe('useDraftLane', () => {
  beforeEach(() => {
    vi.stubGlobal('ref', ref)
    vi.stubGlobal('computed', computed)
  })

  afterEach(() => vi.unstubAllGlobals())

  it('keeps the lane the client assigned, and offers no choice', () => {
    const lane = useDraftLane(ref(draft({ myPosition: 'BOTTOM' })))
    lane.choose('TOP')
    expect(lane.choosable.value).toBe(false)
    expect(lane.draft.value.myPosition).toBe('BOTTOM')
  })

  it('ranks for the free lane until the player chooses another', () => {
    const lane = useDraftLane(ref(draft()))
    expect(lane.choosable.value).toBe(true)
    expect(lane.draft.value.myPosition).toBe('TOP')

    lane.choose('MIDDLE')
    expect(lane.draft.value.myPosition).toBe('MIDDLE')
  })

  it('waits for a choice when no lane is free to infer', () => {
    const lane = useDraftLane(ref(draft({ myTeam: [slot('', true), slot(''), slot(''), slot(''), slot('')] })))
    expect(lane.draft.value.myPosition).toBe('')

    lane.choose('JUNGLE')
    expect(lane.draft.value.myPosition).toBe('JUNGLE')
  })

  it('offers no lane outside a five-lane queue', () => {
    const lane = useDraftLane(ref(draft({ myTeam: [slot('', true), slot('')] })))
    lane.choose('TOP')
    expect(lane.choosable.value).toBe(false)
    expect(lane.draft.value.myPosition).toBe('')
  })
})

describe('useDraftTeamRows', () => {
  beforeEach(() => vi.stubGlobal('computed', computed))
  afterEach(() => vi.unstubAllGlobals())

  const rows = (team: TeamSlot[]) => useDraftTeamRows(ref(draft({ myTeam: team })), ref(null), ref([]))

  it('lays our side out lane by lane once each lane is held once', () => {
    const { byLane, allyRows } = rows(withLane(draft(), 'TOP').myTeam)
    expect(byLane.value).toBe(true)
    expect(allyRows.value.map(row => row.lane)).toEqual(['TOP', 'JUNGLE', 'MIDDLE', 'BOTTOM', 'UTILITY'])
    expect(allyRows.value[0]!.me).toBe(true)
  })

  it('keeps the client\'s order when only we were given a lane, so no ally is dropped', () => {
    const blind = [slot('MIDDLE', true), slot(''), slot(''), slot(''), slot('')]
    const { byLane, allyRows } = rows(blind)
    expect(byLane.value).toBe(false)
    expect(allyRows.value.map(row => row.lane)).toEqual(['MIDDLE', null, null, null, null])
  })
})

describe('DraftSuggestions without a lane from the client', () => {
  const RolePicker = {
    props: ['position'],
    emits: ['update:position'],
    template: '<button data-lane-picker :data-position="position" @click="$emit(\'update:position\', \'JUNGLE\')" />',
  }

  async function strip(position: string, laneChoice: boolean) {
    vi.stubGlobal('ref', ref)
    vi.stubGlobal('computed', computed)
    vi.stubGlobal('useChampionStatics', () => ({ nameOf: () => 'Ahri', champions: ref(new Map()) }))
    vi.stubGlobal('useChampSelectActions', () => ({ writable: ref(true), kind: ref('pick'), hovered: ref(null) }))
    vi.stubGlobal('useDraftCardClick', () => ({ hovers: () => false, status: () => null, click: () => {} }))
    vi.stubGlobal('useTierList', () => ({ entryOf: () => null, status: ref('ready') }))
    vi.stubGlobal('useDamageProfiles', () => ({ profileOf: () => null }))
    vi.stubGlobal('usePlayerRecord', () => ({ games: ref([]) }))
    const { default: DraftSuggestions } = await import('../app/components/draft/DraftSuggestions.vue')
    return mount(DraftSuggestions, {
      props: { mode: 'pick', suggestions: [], hasPool: true, pending: false, position, laneChoice, error: null, allies: [] },
      global: {
        stubs: { RolePicker, UTooltip: { template: '<div><slot /></div>' }, UIcon: true, USwitch: true, UInput: true },
      },
    })
  }

  afterEach(() => vi.unstubAllGlobals())

  it('asks for a lane while none is chosen, and passes the choice up', async () => {
    const wrapper = await strip('', true)
    expect(wrapper.text()).toContain('Choose your lane to rank the picks')
    expect(wrapper.find('[data-lane-picker]').attributes('data-position')).toBeUndefined()

    await wrapper.find('[data-lane-picker]').trigger('click')
    expect(wrapper.emitted('choose-lane')).toEqual([['JUNGLE']])
  })

  it('shows the lane it ranks for in the picker', async () => {
    const wrapper = await strip('TOP', true)
    expect(wrapper.find('[data-lane-picker]').attributes('data-position')).toBe('TOP')
    expect(wrapper.text()).not.toContain('Choose your lane')
  })

  it('offers no picker when the client assigned the lane', async () => {
    expect((await strip('MIDDLE', false)).find('[data-lane-picker]').exists()).toBe(false)
  })
})
