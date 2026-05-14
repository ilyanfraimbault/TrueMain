import type { ItemSetOptionResponse } from '~/types/champions'
import type {
  StaticChampionSpellData,
  StaticItemData,
  StaticSummonerSpellData
} from '~/types/static-data'

export function sortItemsByGoldDesc(items: StaticItemData[]): StaticItemData[] {
  return [...items].sort((left, right) => right.totalGold - left.totalGold || left.id - right.id)
}

export function getStaticItem(
  itemsById: Record<number, StaticItemData>,
  itemId: number
): StaticItemData | null {
  return itemsById[itemId] ?? null
}

export function mapItemIdsToStaticItems(
  itemIds: number[],
  itemsById: Record<number, StaticItemData>
): StaticItemData[] {
  return itemIds
    .map(itemId => getStaticItem(itemsById, itemId))
    .filter((item): item is StaticItemData => item !== null)
}

export function mapItemSetToStaticItems(
  option: ItemSetOptionResponse | null,
  itemsById: Record<number, StaticItemData>
): StaticItemData[] {
  if (!option) {
    return []
  }

  return mapItemIdsToStaticItems(option.itemIds, itemsById)
}

export function getStaticSummonerSpell(
  summonerSpellsById: Record<number, StaticSummonerSpellData>,
  id: number
): StaticSummonerSpellData | null {
  return summonerSpellsById[id] ?? null
}

export function getStaticChampionSpell(
  championSpellsByKey: Record<string, StaticChampionSpellData>,
  sequenceKey: string
): StaticChampionSpellData | null {
  return championSpellsByKey[sequenceKey] ?? null
}

export function mapSkillSequenceToStaticSpells(
  sequence: string[],
  championSpellsByKey: Record<string, StaticChampionSpellData>
): StaticChampionSpellData[] {
  return sequence
    .map(sequenceKey => getStaticChampionSpell(championSpellsByKey, sequenceKey))
    .filter((spell): spell is StaticChampionSpellData => spell !== null)
}
