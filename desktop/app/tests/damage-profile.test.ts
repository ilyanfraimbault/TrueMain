import { describe, expect, it } from 'vitest'
import type { DamageProfile } from '~/utils/damage-profile'
import { indexProfiles, profileOf, teamDamage } from '~/utils/damage-profile'

function measured(championId: number, position: string | null, physical: number, magic: number, damagePerGame: number): DamageProfile {
  return {
    championId,
    position,
    profilePosition: position ?? 'BOTTOM',
    source: 'measured',
    patch: '16.19',
    games: 500,
    physicalShare: physical,
    magicShare: magic,
    trueShare: 1 - physical - magic,
    damagePerGame,
    damageClass: null,
    builds: [],
    flexDamage: false,
  }
}

const fallback: DamageProfile = {
  ...measured(238, null, 0, 0, 0),
  source: 'fallback',
  profilePosition: null,
  patch: null,
  games: null,
  physicalShare: null,
  magicShare: null,
  trueShare: null,
  damagePerGame: null,
  damageClass: 'physical',
}

describe('profileOf', () => {
  const index = indexProfiles([measured(145, 'BOTTOM', 0.6, 0.3, 20_000), measured(145, null, 0.6, 0.3, 20_000), fallback])

  it('reads the lane first, then the champion-wide entry', () => {
    expect(profileOf(index, 145, 'BOTTOM')?.position).toBe('BOTTOM')
    expect(profileOf(index, 145, 'JUNGLE')?.position).toBeNull()
    expect(profileOf(index, 238, 'MIDDLE')?.source).toBe('fallback')
  })

  it('is null for a champion nothing is known about', () => {
    expect(profileOf(index, 1, 'TOP')).toBeNull()
  })
})

describe('teamDamage', () => {
  it('weights each pick by the damage it deals', () => {
    const team = teamDamage([measured(1, 'BOTTOM', 0.9, 0.1, 30_000), measured(2, 'UTILITY', 0.1, 0.9, 10_000), fallback, null])
    expect(team?.magicShare).toBeCloseTo((0.1 * 30_000 + 0.9 * 10_000) / 40_000)
    expect(team!.physicalShare + team!.magicShare + team!.trueShare).toBeCloseTo(1)
    expect(team?.measured).toBe(2)
    expect(team?.unmeasured).toBe(2)
  })

  it('never invents a share for a team with nothing measured', () => {
    expect(teamDamage([fallback, null])).toBeNull()
  })
})
