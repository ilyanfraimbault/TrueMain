import type { Moment } from '~/types/recordings'
import { describe, expect, it } from 'vitest'
import { momentCast, momentDuel, momentLabel, objectiveIconUrl } from '~/utils/recording-moments'

const moment = (kind: Moment['kind'], extra: Partial<Moment> = {}): Moment =>
  ({ kind, videoMs: 0, endVideoMs: 0, kills: kind === 'kill' ? 1 : 0, ally: null, killerId: null, victimIds: [], dragon: null, ...extra })

// The player plays Ahri (103) as participant 3; Zed (238) is 7, Lux (99) is 8, Thresh (412) is 4.
const cast = momentCast(103, { champions: [
  { participantId: 3, championId: 103 },
  { participantId: 4, championId: 412 },
  { participantId: 7, championId: 238 },
  { participantId: 8, championId: 99 },
] })

describe('a moment in champions', () => {
  it('names the player the killer of their kills, every victim after', () => {
    expect(momentDuel(moment('kill', { kills: 2, victimIds: [7, 8] }), cast)).toEqual({ killer: 103, victims: [238, 99] })
  })

  it('names the player the victim of their deaths', () => {
    expect(momentDuel(moment('death', { killerId: 7 }), cast)).toEqual({ killer: 238, victims: [103] })
    // An execution: no champion landed it.
    expect(momentDuel(moment('death'), cast)).toEqual({ killer: null, victims: [103] })
  })

  it('names the ally who landed an assisted kill', () => {
    expect(momentDuel(moment('assist', { killerId: 4, victimIds: [7] }), cast)).toEqual({ killer: 412, victims: [238] })
  })

  it('names no one without a scoreboard or for an objective', () => {
    const bare = momentCast(null, null)
    expect(momentDuel(moment('assist', { killerId: 4, victimIds: [7] }), bare)).toBeNull()
    expect(momentDuel(moment('kill', { victimIds: [7] }), momentCast(103, null))).toEqual({ killer: 103, victims: [] })
    expect(momentDuel(moment('baron', { ally: true }), cast)).toBeNull()
  })
})

describe('an objective', () => {
  it('reads its drake\'s element when it was kept', () => {
    expect(momentLabel(moment('dragon', { ally: true, dragon: 'infernal' }))).toBe('Infernal Drake')
    expect(momentLabel(moment('dragon', { ally: false, dragon: 'ocean' }))).toBe('Enemy Ocean Drake')
    expect(momentLabel(moment('dragon', { ally: true }))).toBe('Dragon')
  })

  it('is drawn with the game\'s minimap icon of it', () => {
    expect(objectiveIconUrl(moment('dragon', { dragon: 'chemtech' }))).toMatch(/\/dragon_chemtech\.png$/)
    expect(objectiveIconUrl(moment('dragon'))).toMatch(/\/dragon\.png$/)
    expect(objectiveIconUrl(moment('herald'))).toMatch(/\/riftherald\.png$/)
    expect(objectiveIconUrl(moment('kill'))).toBeNull()
  })
})
