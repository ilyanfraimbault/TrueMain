import { describe, expect, it } from 'vitest'
import fixture from '../../../backend/tests/TrueMain.UnitTests/Fixtures/team-damage-share.json'
import type { DamageProfile } from '~/utils/damage-profile'
import { teamDamage } from '~/utils/damage-profile'
import { DAMAGE_THRESHOLDS, band, damageNote, dominant, draftPush, isUsable } from '~/utils/draft-damage'

function measured(championId: number, physical: number, magic: number, damagePerGame = 20_000, flexDamage = false): DamageProfile {
  return {
    championId,
    position: 'MIDDLE',
    profilePosition: 'MIDDLE',
    source: 'measured',
    patch: '16.19',
    games: 500,
    physicalShare: physical,
    magicShare: magic,
    trueShare: 1 - physical - magic,
    damagePerGame,
    damageClass: null,
    builds: [],
    flexDamage,
  }
}

const unknown: DamageProfile = {
  ...measured(999, 0, 0),
  source: 'fallback',
  physicalShare: null,
  magicShare: null,
  trueShare: null,
  damagePerGame: null,
  damageClass: 'magic',
}

describe('the shared fixture (C# DraftAxisEvaluator ↔ the draft bar)', () => {
  for (const entry of fixture.cases) {
    it(entry.name, () => {
      const picks = [
        ...entry.picks.map((pick, index) => measured(index + 1, pick.physicalShare, pick.magicShare, pick.damagePerGame)),
        ...Array.from({ length: entry.missing }, () => unknown),
      ]
      const team = teamDamage(picks)
      if (entry.expected === null) {
        expect(isUsable(team)).toBe(false)
        return
      }
      expect(isUsable(team)).toBe(true)
      expect(team!.physicalShare).toBeCloseTo(entry.expected.physicalShare, 9)
      expect(team!.magicShare).toBeCloseTo(entry.expected.magicShare, 9)
      expect(team!.trueShare).toBeCloseTo(entry.expected.trueShare, 9)
      const t = DAMAGE_THRESHOLDS
      expect(band(team!.magicShare, t.enemyMagicShareLow, t.enemyMagicShareHigh)).toBe(entry.expected.enemyMagicDamage)
      expect(band(team!.physicalShare, t.enemyPhysicalShareLow, t.enemyPhysicalShareHigh)).toBe(entry.expected.enemyPhysicalDamage)
      expect(band(team!.magicShare, t.allyMagicShareLow, t.allyMagicShareHigh)).toBe(entry.expected.allyMagicDamage)
    })
  }
})

describe('dominant', () => {
  it('names the type the team deals most of', () => {
    expect(dominant({ physicalShare: 0.6, magicShare: 0.3, trueShare: 0.1, measured: 3, unmeasured: 0 })).toEqual({ kind: 'physical', share: 0.6 })
    expect(dominant({ physicalShare: 0.2, magicShare: 0.7, trueShare: 0.1, measured: 3, unmeasured: 0 }).kind).toBe('magic')
  })
})

describe('damageNote', () => {
  const physicalTeam = [measured(1, 0.9, 0.05), measured(2, 0.85, 0.1)]
  const magicTeam = [measured(1, 0.1, 0.85), measured(2, 0.2, 0.75)]

  it('says a magic pick answers a physical team, and the reverse', () => {
    expect(damageNote(physicalTeam, measured(10, 0.1, 0.85))?.adds).toBe('magic')
    expect(damageNote(magicTeam, measured(10, 0.9, 0.05))?.adds).toBe('physical')
  })

  it('says nothing when the pick does not answer the team', () => {
    expect(damageNote(physicalTeam, measured(10, 0.9, 0.05))).toBeNull()
    expect(damageNote(physicalTeam, measured(10, 0.5, 0.4))).toBeNull()
  })

  it('needs two measured allies, at most one unknown, and a measured one-build candidate', () => {
    expect(damageNote([measured(1, 0.9, 0.05)], measured(10, 0.1, 0.85))).toBeNull()
    expect(damageNote([...physicalTeam, unknown, unknown], measured(10, 0.1, 0.85))).toBeNull()
    expect(damageNote(physicalTeam, unknown)).toBeNull()
    expect(damageNote(physicalTeam, measured(10, 0.1, 0.85, 20_000, true))).toBeNull()
  })
})

describe('draftPush', () => {
  const reason = { axis: 'EnemyMagicDamage', bucket: 'High' as const }

  it('picks the candidate this draft moves up most, among those a situation moved', () => {
    const push = draftPush('boots', { candidates: [
      { itemId: 3047, share: 0.5, baseShare: 0.6, reasons: [] },
      { itemId: 3111, share: 0.4, baseShare: 0.15, reasons: [reason] },
      { itemId: 3020, share: 0.1, baseShare: 0.05, reasons: [reason] },
    ] })
    expect(push).toEqual({ slot: 'boots', itemId: 3111, share: 0.4, baseShare: 0.15, reason })
  })

  it('is null when the draft moves nothing up', () => {
    expect(draftPush('build', { candidates: [{ itemId: 1, share: 0.3, baseShare: 0.4, reasons: [reason] }, { itemId: 2, share: 0.6, baseShare: 0.5, reasons: [] }] })).toBeNull()
    expect(draftPush('build', null)).toBeNull()
  })

  it('ignores a shift too small to be advice', () => {
    expect(draftPush('build', { candidates: [{ itemId: 6673, share: 0.21, baseShare: 0.2, reasons: [reason] }] })).toBeNull()
    // Five points, but on an item already taken most of the time: not a quarter more often.
    expect(draftPush('build', { candidates: [{ itemId: 1, share: 0.65, baseShare: 0.6, reasons: [reason] }] })).toBeNull()
  })

  it('gives only an enemy or lane situation as the reason', () => {
    const ally = { axis: 'AllyMagicDamage', bucket: 'High' as const }
    expect(draftPush('build', { candidates: [{ itemId: 6673, share: 0.4, baseShare: 0.2, reasons: [ally] }] })).toBeNull()
    expect(draftPush('build', { candidates: [{ itemId: 6673, share: 0.4, baseShare: 0.2, reasons: [ally, reason] }] }))
      .toEqual({ slot: 'build', itemId: 6673, share: 0.4, baseShare: 0.2, reason })
  })
})
