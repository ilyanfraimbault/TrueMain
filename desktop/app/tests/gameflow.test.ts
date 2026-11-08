import { describe, expect, it } from 'vitest'
import type { GameflowPhase } from '~/types/lcu'
import { isChampSelect, isGameRunning, isPostGame } from '~/utils/gameflow'

/** The phase groupings mirror `GameflowPhase`'s in `crates/lcu/src/model.rs` (#2472). */

const ALL: GameflowPhase[] = ['None', 'Lobby', 'Matchmaking', 'ReadyCheck', 'ChampSelect', 'InProgress', 'Reconnect', 'WaitingForStats', 'PreEndOfGame', 'EndOfGame', 'Unknown']

describe('gameflow groupings', () => {
  it('names the same phases as the shell', () => {
    expect(ALL.filter(isChampSelect)).toEqual(['ChampSelect'])
    expect(ALL.filter(isGameRunning)).toEqual(['InProgress', 'Reconnect'])
    expect(ALL.filter(isPostGame)).toEqual(['WaitingForStats', 'PreEndOfGame', 'EndOfGame'])
  })

  it('puts each phase in one grouping at most', () => {
    for (const phase of ALL) {
      expect([isChampSelect, isGameRunning, isPostGame].filter(group => group(phase)).length).toBeLessThanOrEqual(1)
    }
  })
})
