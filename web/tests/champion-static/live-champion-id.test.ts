import { describe, expect, it } from 'vitest'
import { ALTERNATE_MODE_CHAMPION_ID_FLOOR, isLiveChampionId } from '~~/shared/utils/ddragon'

describe('isLiveChampionId', () => {
  it('keeps the real Riot champion id range', () => {
    // Annie (first) and Naafiri (highest key shipped on 16.15).
    expect([1, 103, 950].every(isLiveChampionId)).toBe(true)
  })

  it('drops the League classique (Jade) kits', () => {
    // `Jade_Ahri` / `Jade_Annie`: 60000 + the base champion key.
    expect(isLiveChampionId(60103)).toBe(false)
    expect(isLiveChampionId(60001)).toBe(false)
  })

  it('drops non-numeric and non-positive ids', () => {
    // CDragon's champion summary carries a `-1` "None" entry.
    expect(isLiveChampionId(Number('Jade_Ahri'))).toBe(false)
    expect(isLiveChampionId(-1)).toBe(false)
    expect(isLiveChampionId(0)).toBe(false)
  })

  it('cuts exactly at the floor', () => {
    expect(isLiveChampionId(ALTERNATE_MODE_CHAMPION_ID_FLOOR - 1)).toBe(true)
    expect(isLiveChampionId(ALTERNATE_MODE_CHAMPION_ID_FLOOR)).toBe(false)
  })
})
