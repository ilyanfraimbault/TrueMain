import { describe, expect, it } from 'vitest'
import { roamVerdict, ROAMER_KP15 } from '~/utils/roam-verdict'

describe('roamVerdict', () => {
  it('flags a champion that clears the roamer threshold', () => {
    expect(roamVerdict(2.4)?.label).toBe('Roamer')
    expect(roamVerdict(ROAMER_KP15)?.label).toBe('Roamer')
  })

  it('says nothing about a champion that stays in lane', () => {
    // The badge is one-sided on purpose: "balanced" and "lane-focused" are the
    // default a champion page already implies, so they earn no chip.
    expect(roamVerdict(ROAMER_KP15 - 0.1)).toBeNull()
    expect(roamVerdict(1.3)).toBeNull()
    expect(roamVerdict(0)).toBeNull()
  })

  it('withholds the badge when the metric was never measured', () => {
    // null is the backend's "below the sample floor, or JUNGLE" — an unmeasured
    // champion must not read as a non-roamer any more than as a roamer, and the
    // absent badge happens to say exactly that.
    expect(roamVerdict(null)).toBeNull()
    expect(roamVerdict(undefined)).toBeNull()
  })

  it('puts the number behind the call in the tooltip', () => {
    expect(roamVerdict(2.37)?.tooltip).toBe('2.4 out-of-lane kills + assists per game by 15 min')
  })
})
