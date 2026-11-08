import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, ref } from 'vue'
import type { DraftState, TeamSlot } from '~/types/lcu'
import { useDraftLane } from '~/composables/useDraftLane'
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
