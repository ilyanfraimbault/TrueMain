import type {
  ChampionStaticData,
  StaticChampionSpellData,
  StaticItemData,
  StaticPerkData,
  StaticPerkStyleData,
  StaticSummonerSpellData,
} from '~~/shared/types/static-data'
import {
  getChampionSpellImageUrl,
  getSummonerSpellImageUrl,
  normalizeDataDragonPatch,
} from '~~/shared/utils/ddragon'

export const COMMUNITY_DRAGON_PREFIX
  = 'https://raw.communitydragon.org/latest/plugins/rcp-be-lol-game-data/global/default'

export const EMPTY_STATIC_DATA: ChampionStaticData = {
  championName: null,
  championIconUrl: null,
  items: {},
  summonerSpells: {},
  championSpells: {},
  perks: {},
  perkStyles: {},
}

export interface CdragonPerkRow {
  id: number
  name: string
  iconPath: string
  shortDesc?: string
  longDesc?: string
}

export interface CdragonPerkStyleRow {
  id: number
  name: string
  iconPath: string
}

type ItemDataResponse = {
  data: Record<string, {
    name: string
    image: { full: string }
    gold: { total: number }
    plaintext?: string
    description?: string
  }>
}
type SummonerDataResponse = {
  data: Record<string, {
    key: string
    name: string
    image: { full: string }
    description?: string
    cooldown?: number[]
    summonerLevel?: number
  }>
}
type ChampionListResponse = { data: Record<string, { id: string, key: string, name: string, image: { full: string } }> }
type ChampionDetailResponse = {
  data: Record<string, {
    spells: Array<{
      name: string
      image: { full: string }
      description?: string
      cooldownBurn?: string
      costBurn?: string
      costType?: string
      rangeBurn?: string
    }>
  }>
}

export function rewriteCdragonAsset(iconPath: string): string {
  if (!iconPath) return ''
  return iconPath.toLowerCase().replace(/^\/lol-game-data\/assets/, COMMUNITY_DRAGON_PREFIX)
}

/**
 * Build the perk id→data map from a CDragon perks.json response. Shared by
 * the per-champion static endpoint and the standalone rune-tree endpoint so
 * the `StaticPerkData` shape stays in lockstep across both code paths.
 */
export function buildPerkMap(perks: CdragonPerkRow[]): Record<number, StaticPerkData> {
  return Object.fromEntries(
    perks.map(perk => [perk.id, {
      id: perk.id,
      name: perk.name,
      iconUrl: rewriteCdragonAsset(perk.iconPath),
      shortDesc: perk.shortDesc,
      longDesc: perk.longDesc,
    }]),
  )
}

export function buildPerkStyleMap(styles: CdragonPerkStyleRow[]): Record<number, StaticPerkStyleData> {
  return Object.fromEntries(
    styles.map(style => [style.id, {
      id: style.id,
      name: style.name,
      iconUrl: rewriteCdragonAsset(style.iconPath),
    }]),
  )
}

async function resolveLatestPatch(): Promise<string | null> {
  const versions = await $fetch<string[]>('https://ddragon.leagueoflegends.com/api/versions.json').catch(() => [])
  return versions[0] ?? null
}

const SPELL_SLOTS = ['Q', 'W', 'E', 'R'] as const

export async function loadStaticData(championId: number, patch: string | null): Promise<ChampionStaticData> {
  const normalized = normalizeDataDragonPatch(patch) ?? await resolveLatestPatch()
  if (!normalized) return EMPTY_STATIC_DATA

  const [items, spells, champs, perks, perkStyles] = await Promise.all([
    $fetch<ItemDataResponse>(`https://ddragon.leagueoflegends.com/cdn/${normalized}/data/en_US/item.json`).catch(() => ({ data: {} })),
    $fetch<SummonerDataResponse>(`https://ddragon.leagueoflegends.com/cdn/${normalized}/data/en_US/summoner.json`).catch(() => ({ data: {} })),
    $fetch<ChampionListResponse>(`https://ddragon.leagueoflegends.com/cdn/${normalized}/data/en_US/champion.json`).catch(() => ({ data: {} })),
    $fetch<CdragonPerkRow[]>(`${COMMUNITY_DRAGON_PREFIX}/v1/perks.json`).catch(() => [] as CdragonPerkRow[]),
    $fetch<{ styles: CdragonPerkStyleRow[] }>(`${COMMUNITY_DRAGON_PREFIX}/v1/perkstyles.json`).catch(() => ({ styles: [] as CdragonPerkStyleRow[] })),
  ])

  const itemMap: Record<number, StaticItemData> = Object.fromEntries(
    Object.entries(items.data).map(([id, item]) => [
      Number(id),
      {
        id: Number(id),
        name: item.name,
        iconUrl: `https://ddragon.leagueoflegends.com/cdn/${normalized}/img/item/${item.image.full}`,
        totalGold: item.gold.total,
        plaintext: item.plaintext,
        description: item.description,
      },
    ]),
  )

  const summonerMap: Record<number, StaticSummonerSpellData> = Object.fromEntries(
    Object.values(spells.data).map(spell => [
      Number(spell.key),
      {
        id: Number(spell.key),
        name: spell.name,
        iconUrl: getSummonerSpellImageUrl(spell.image.full, normalized) ?? '',
        description: spell.description,
        cooldown: spell.cooldown?.[0],
        summonerLevel: spell.summonerLevel,
      },
    ]),
  )

  const perkMap = buildPerkMap(perks)
  const perkStyleMap = buildPerkStyleMap(perkStyles.styles ?? [])

  const summary = Object.values(champs.data).find(c => Number(c.key) === championId)
  if (!summary) {
    return { ...EMPTY_STATIC_DATA, items: itemMap, summonerSpells: summonerMap, perks: perkMap, perkStyles: perkStyleMap }
  }

  const detail = await $fetch<ChampionDetailResponse>(
    `https://ddragon.leagueoflegends.com/cdn/${normalized}/data/en_US/champion/${summary.id}.json`,
  ).catch((): ChampionDetailResponse => ({ data: {} }))

  const championSpells: Record<string, StaticChampionSpellData> = Object.fromEntries(
    (detail.data[summary.id]?.spells ?? []).slice(0, SPELL_SLOTS.length).flatMap((spell, index) => {
      const key = SPELL_SLOTS[index]
      if (!key) return []
      return [[key, {
        key,
        name: spell.name,
        iconUrl: getChampionSpellImageUrl(spell.image.full, normalized) ?? '',
        description: spell.description,
        cooldownBurn: spell.cooldownBurn,
        costBurn: spell.costBurn,
        costType: spell.costType,
        rangeBurn: spell.rangeBurn,
      }]]
    }),
  )

  return {
    championName: summary.name,
    championIconUrl: `https://ddragon.leagueoflegends.com/cdn/${normalized}/img/champion/${summary.image.full}`,
    items: itemMap,
    summonerSpells: summonerMap,
    championSpells,
    perks: perkMap,
    perkStyles: perkStyleMap,
  }
}
