import { describe, expect, it } from 'vitest'
import type { StaticItemData } from '#shared/types/static-data'
import { sampleGame } from '~/utils/overlay-sample'
import { basketName, starterBought, starterCost, starterRequest } from '~/utils/next-item'

function item(id: number, totalGold: number, tags: string[]): StaticItemData {
  return { id, name: `Item ${id}`, iconUrl: '', totalGold, tags }
}

const items: Record<number, StaticItemData> = {
  1055: item(1055, 450, ['Damage', 'Health', 'Lane']),
  2003: item(2003, 50, ['Consumable', 'Health']),
  2055: item(2055, 75, ['Consumable', 'Vision']),
  3340: item(3340, 0, ['Trinket', 'Vision']),
  1036: item(1036, 350, ['Damage']),
}

describe('starterBought', () => {
  it('waits while only the trinket and potions are held', () => {
    expect(starterBought([], items)).toBe(false)
    expect(starterBought([3340], items)).toBe(false)
    expect(starterBought([2003, 2055, 3340], items)).toBe(false)
  })

  it('is done once anything else is held, the starter advised or not', () => {
    expect(starterBought([1055, 2003, 3340], items)).toBe(true)
    expect(starterBought([1036, 3340], items)).toBe(true)
  })

  it('reads nothing into an item the static data does not know', () => {
    expect(starterBought([999999, 3340], items)).toBe(false)
  })
})

describe('starterCost', () => {
  it('prices each item of the basket as often as it is listed', () => {
    expect(starterCost([1055, 2003], items)).toBe(500)
    expect(starterCost([1055, 2003, 2003], items)).toBe(550)
  })
})

describe('basketName', () => {
  it('names each item once, with its count when listed more than once', () => {
    const name = (id: number) => items[id]?.name ?? `Item ${id}`
    expect(basketName([1055, 2003], name)).toBe('Item 1055 + Item 2003')
    expect(basketName([1055, 2003, 2003], name)).toBe('Item 1055 + Item 2003 ×2')
  })
})

describe('starterRequest', () => {
  const ids: Record<string, number> = { aatrox: 266, vi: 254, ahri: 103, jinx: 222, thresh: 412, malphite: 54, viego: 234, orianna: 61, ezreal: 81, nautilus: 111 }
  const championIdOf = (alias: string) => ids[alias.toLowerCase()] ?? null

  it('sends our lane and every other player on theirs, by side', () => {
    const request = starterRequest(sampleGame(), championIdOf)
    expect(request?.championId).toBe(103)
    expect(request?.body.position).toBe('MIDDLE')
    expect(request?.body.allies).toEqual([
      { championId: 266, position: 'TOP' },
      { championId: 254, position: 'JUNGLE' },
      { championId: 222, position: 'BOTTOM' },
      { championId: 412, position: 'UTILITY' },
    ])
    expect(request?.body.enemies.map(slot => slot.position)).toEqual(['TOP', 'JUNGLE', 'MIDDLE', 'BOTTOM', 'UTILITY'])
  })

  it('leaves out a player the game names no lane for', () => {
    const game = sampleGame()
    game.players[5]!.position = ''
    expect(starterRequest(game, championIdOf)?.body.enemies).toHaveLength(4)
  })

  it('asks nothing without a lane of our own', () => {
    const game = sampleGame()
    game.players.find(player => player.isMe)!.position = ''
    expect(starterRequest(game, championIdOf)).toBeNull()
  })
})
