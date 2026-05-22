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

const COMMUNITY_DRAGON_BASE = 'https://raw.communitydragon.org'
const COMMUNITY_DRAGON_PATH_SUFFIX = 'plugins/rcp-be-lol-game-data/global/default'
// Reject anything that isn't a strict `major.minor` or `major.minor.patch` so
// a hostile `?patch=` query can't traverse out of the CDragon prefix.
const PATCH_FORMAT_RE = /^\d+\.\d+(?:\.\d+)?$/

/**
 * CommunityDragon serves the same asset tree under both `/latest` and the
 * patch-specific path `/<major>.<minor>` (e.g. `/15.10`). The per-patch URLs
 * return a year-long `cache-control` max-age, while `latest` redirects with a
 * much shorter TTL. Pinning to the patch lets IPX (and the browser) keep
 * served bytes around for as long as we ask without going stale on a new
 * patch release.
 *
 * @param patch - Either a major.minor patch (`15.10`) or a full DDragon
 *   version (`15.10.1`). Anything falsy falls back to `latest`.
 */
export function communityDragonPrefix(patch?: string | null): string {
  const version = communityDragonVersion(patch)
  return `${COMMUNITY_DRAGON_BASE}/${version}/${COMMUNITY_DRAGON_PATH_SUFFIX}`
}

function communityDragonVersion(patch?: string | null): string {
  if (!patch || !PATCH_FORMAT_RE.test(patch)) return 'latest'
  // DDragon patches look like `15.10.1`; CommunityDragon expects `15.10`.
  const segments = patch.split('.')
  return `${segments[0]}.${segments[1]}`
}

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
    partype?: string
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

/**
 * DDragon ships some champion spell `costType` values as the literal
 * placeholder `" {{ abilityresourcename }}"` (or `(( ... ))`) instead of the
 * resolved resource name. Substitute the champion-level `partype` (Mana,
 * Energy, Blood Well, ...) so the tooltip shows "Cost: 65 Mana" rather than
 * "Cost: 65 {{ abilityresourcename }}".
 */
function resolveCostType(costType: string | undefined, partype: string): string | undefined {
  if (!costType) return costType
  const resolved = costType
    .replace(/\{\{\s*abilityresourcename\s*\}\}/gi, partype)
    .replace(/\(\(\s*abilityresourcename\s*\)\)/gi, partype)
  // If `partype` was missing/empty we'd end up with just the placeholder's
  // surrounding whitespace (e.g. " Mana" → " "). Treat that as "no resource
  // name available" rather than letting the UI render "Cost: 50 " with a
  // blank resource label.
  return resolved.trim() === '' ? undefined : resolved
}

export function rewriteCdragonAsset(iconPath: string, patch?: string | null): string {
  if (!iconPath) return ''
  return iconPath.toLowerCase().replace(/^\/lol-game-data\/assets/, communityDragonPrefix(patch))
}

/**
 * Build the perk id→data map from a CDragon perks.json response. Shared by
 * the per-champion static endpoint and the standalone rune-tree endpoint so
 * the `StaticPerkData` shape stays in lockstep across both code paths.
 *
 * @param patch - If provided, icon URLs are rewritten to the per-patch CDN
 *   path (long cache-control). Falls back to `latest` when omitted for
 *   backwards compatibility.
 */
export function buildPerkMap(perks: CdragonPerkRow[], patch?: string | null): Record<number, StaticPerkData> {
  return Object.fromEntries(
    perks.map(perk => [perk.id, {
      id: perk.id,
      name: perk.name,
      iconUrl: rewriteCdragonAsset(perk.iconPath, patch),
      shortDesc: perk.shortDesc,
      longDesc: perk.longDesc,
    }]),
  )
}

export function buildPerkStyleMap(styles: CdragonPerkStyleRow[], patch?: string | null): Record<number, StaticPerkStyleData> {
  return Object.fromEntries(
    styles.map(style => [style.id, {
      id: style.id,
      name: style.name,
      iconUrl: rewriteCdragonAsset(style.iconPath, patch),
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

  // Pin CDragon to the same patch (e.g. /15.10/...) so its responses become
  // year-cacheable rather than rolling on `latest`.
  const cdragonPrefix = communityDragonPrefix(normalized)

  const [items, spells, champs, perks, perkStyles] = await Promise.all([
    $fetch<ItemDataResponse>(`https://ddragon.leagueoflegends.com/cdn/${normalized}/data/en_US/item.json`).catch(() => ({ data: {} })),
    $fetch<SummonerDataResponse>(`https://ddragon.leagueoflegends.com/cdn/${normalized}/data/en_US/summoner.json`).catch(() => ({ data: {} })),
    $fetch<ChampionListResponse>(`https://ddragon.leagueoflegends.com/cdn/${normalized}/data/en_US/champion.json`).catch(() => ({ data: {} })),
    $fetch<CdragonPerkRow[]>(`${cdragonPrefix}/v1/perks.json`).catch(() => [] as CdragonPerkRow[]),
    $fetch<{ styles: CdragonPerkStyleRow[] }>(`${cdragonPrefix}/v1/perkstyles.json`).catch(() => ({ styles: [] as CdragonPerkStyleRow[] })),
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

  const perkMap = buildPerkMap(perks, normalized)
  const perkStyleMap = buildPerkStyleMap(perkStyles.styles ?? [], normalized)

  const summary = Object.values(champs.data).find(c => Number(c.key) === championId)
  if (!summary) {
    return { ...EMPTY_STATIC_DATA, items: itemMap, summonerSpells: summonerMap, perks: perkMap, perkStyles: perkStyleMap }
  }

  const detail = await $fetch<ChampionDetailResponse>(
    `https://ddragon.leagueoflegends.com/cdn/${normalized}/data/en_US/champion/${summary.id}.json`,
  ).catch((): ChampionDetailResponse => ({ data: {} }))

  const partype = detail.data[summary.id]?.partype ?? ''
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
        costType: resolveCostType(spell.costType, partype),
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
