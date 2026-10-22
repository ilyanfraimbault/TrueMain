import { describe, expect, it } from 'vitest'
import { ITEM_CONTEXT_TONE_CLASS, KNOWN_ITEM_CONTEXT_AXES } from '#shared/utils/item-context'
import { BUILT_AXES, builtAxisPhrase } from '~/utils/built-reasons'
import { isDraftReason } from '~/utils/draft-damage'
import { nextItemRequest } from '~/utils/next-item'
import { sampleGame } from '~/utils/overlay-sample'

// The in-game axes of what the enemies have built (#1750), from `ItemContextTypes.cs`
// (`ItemContextAxis`, the members marked `[InGame(OverlayOnly = true)]`). A hand-kept mirror,
// like the site's: it fails when the fold gains one the app has no sentence for.
const BACKEND_BUILT_AXES = [
  'EnemyArmorBuilt',
  'EnemyMagicResistBuilt',
  'OpponentArmorBuilt',
  'OpponentMagicResistBuilt',
  'EnemyHealingBuilt',
  'EnemyMagicDamageBuilt',
  'EnemyCritBuilt',
]

describe('builtAxisPhrase', () => {
  it('words every in-game axis at both ends, as a game state', () => {
    expect([...BUILT_AXES].sort()).toEqual([...BACKEND_BUILT_AXES].sort())
    for (const axis of BACKEND_BUILT_AXES) {
      for (const bucket of ['High', 'Low'] as const) {
        const tokens = builtAxisPhrase(axis, bucket)
        const text = tokens?.map(token => token.text).join('')
        expect(text, `${axis} ${bucket}`).toMatch(/^(once|while) /)
        for (const token of tokens ?? []) {
          if (token.tone) expect(ITEM_CONTEXT_TONE_CLASS[token.tone], `${axis} ${token.text}`).toBeTruthy()
        }
      }
    }
  })

  it('leaves the site\'s axes to the site', () => {
    for (const axis of BACKEND_BUILT_AXES) expect(KNOWN_ITEM_CONTEXT_AXES).not.toContain(axis)
    expect(builtAxisPhrase('EnemyMagicDamage', 'High')).toBeNull()
  })

  it('reads the damage built from both ends of one share', () => {
    expect(builtAxisPhrase('EnemyMagicDamageBuilt', 'High')?.find(token => token.tone)?.text).toBe('magic damage')
    expect(builtAxisPhrase('EnemyMagicDamageBuilt', 'Low')?.find(token => token.tone)?.text).toBe('physical damage')
  })

  it('is never a draft reason: nothing is built in champion select', () => {
    for (const axis of BACKEND_BUILT_AXES) expect(isDraftReason(axis)).toBe(false)
    expect(isDraftReason('EnemyMagicDamage')).toBe(true)
  })
})

describe('nextItemRequest', () => {
  const ids: Record<string, number> = { aatrox: 266, vi: 254, ahri: 103, jinx: 222, thresh: 412, malphite: 54, viego: 234, orianna: 61, ezreal: 81, nautilus: 111 }
  const championIdOf = (alias: string) => ids[alias.toLowerCase()] ?? null

  it('sends what each enemy holds, and nothing of the allies', () => {
    const game = sampleGame()
    const request = nextItemRequest(game, [], championIdOf)
    const enemies = game.players.filter(player => player.team !== game.players.find(p => p.isMe)!.team)
    expect(request?.body.enemies).toHaveLength(5)
    expect(request?.body.enemies.map(enemy => enemy.items)).toEqual(enemies.map(enemy => enemy.items.map(item => item.itemId)))
    expect(request?.body.allies.every(ally => !('items' in ally))).toBe(true)
  })
})
