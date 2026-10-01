/**
 * The site's static-data shapes, built from Data Dragon and Community Dragon
 * responses. Ported from the site's Nitro handlers — `web/server/api/static/
 * {items,summoner-spells,rune-tree}.get.ts` and `web/server/utils/ddragon-
 * loader.ts` — which the desktop app cannot call: it has no server, so the
 * webview fetches the same files and shapes them the same way. The output
 * types are the site's own (`shared/types/static-data.ts`), so the twinned
 * tooltip and core-view components read them unchanged. Keep the mapping in
 * step with those handlers.
 */
import type {
  ChampionStaticData,
  RuneTreeResponse,
  RuneTreeStyle,
  StaticChampionSpellData,
  StaticItemData,
  StaticPerkData,
  StaticPerkStyleData,
  StaticSummonerSpellData,
} from '#shared/types/static-data'
import { getChampionSpellImageUrl, getSummonerSpellImageUrl } from '#shared/utils/ddragon'

export const DDRAGON = 'https://ddragon.leagueoflegends.com/cdn'
const COMMUNITY_DRAGON_BASE = 'https://raw.communitydragon.org'
const COMMUNITY_DRAGON_PATH_SUFFIX = 'plugins/rcp-be-lol-game-data/global/default'
const PATCH_FORMAT_RE = /^\d+\.\d+(?:\.\d+)?$/

/** `15.10.1` → `15.10`; anything else → `latest`. */
function communityDragonVersion(patch?: string | null): string {
  if (!patch || !PATCH_FORMAT_RE.test(patch)) return 'latest'
  const segments = patch.split('.')
  return `${segments[0]}.${segments[1]}`
}

export function communityDragonPrefix(patch?: string | null): string {
  return `${COMMUNITY_DRAGON_BASE}/${communityDragonVersion(patch)}/${COMMUNITY_DRAGON_PATH_SUFFIX}`
}

function rewriteCdragonAsset(iconPath: string, patch?: string | null): string {
  if (!iconPath) return ''
  return iconPath.toLowerCase().replace(/^\/lol-game-data\/assets/, communityDragonPrefix(patch))
}

export interface ItemListResponse {
  data: Record<string, {
    name: string
    image: { full: string }
    gold?: { total?: number, purchasable?: boolean }
    inStore?: boolean
    plaintext?: string
    description?: string
    tags?: string[]
  }>
}

export function toItemsMap(items: ItemListResponse, patch: string): Record<number, StaticItemData> {
  return Object.fromEntries(
    Object.entries(items.data).map(([id, item]) => [
      Number(id),
      {
        id: Number(id),
        name: item.name,
        iconUrl: `${DDRAGON}/${patch}/img/item/${item.image.full}`,
        totalGold: item.gold?.total ?? 0,
        purchasable: item.gold?.purchasable,
        inStore: item.inStore,
        plaintext: item.plaintext,
        description: item.description,
        tags: item.tags,
      } satisfies StaticItemData,
    ]),
  )
}

export interface SummonerListResponse {
  data: Record<string, {
    key: string
    name: string
    image: { full: string }
    description?: string
    cooldown?: number[]
    summonerLevel?: number
  }>
}

export function toSummonersMap(spells: SummonerListResponse, patch: string): Record<number, StaticSummonerSpellData> {
  return Object.fromEntries(
    Object.values(spells.data).map(spell => [
      Number(spell.key),
      {
        id: Number(spell.key),
        name: spell.name,
        iconUrl: getSummonerSpellImageUrl(spell.image.full, patch) ?? '',
        description: spell.description,
        cooldown: spell.cooldown?.[0],
        summonerLevel: spell.summonerLevel,
      } satisfies StaticSummonerSpellData,
    ]),
  )
}

interface CdragonPerkRow {
  id: number
  name: string
  iconPath: string
  shortDesc?: string
  longDesc?: string
}

interface CdragonPerkStyleRow {
  id: number
  name: string
  iconPath: string
  tooltip?: string
  slots: Array<{ type: string, perks: number[] }>
}

/**
 * The rune tree for a patch. Community Dragon publishes a patch's directory
 * after Data Dragon lists the version, so a pinned URL that 404s early in a
 * patch cycle falls back to `latest`, as the site does.
 */
export async function fetchRuneTree(patch: string | null): Promise<RuneTreeResponse> {
  try {
    return await fetchRuneTreeAt(patch)
  }
  catch (error) {
    if (patch === null) throw error
    return fetchRuneTreeAt(null)
  }
}

async function fetchRuneTreeAt(patch: string | null): Promise<RuneTreeResponse> {
  const prefix = communityDragonPrefix(patch)
  const [perks, perkStyles] = await Promise.all([
    $fetch<CdragonPerkRow[]>(`${prefix}/v1/perks.json`),
    $fetch<{ styles: CdragonPerkStyleRow[] }>(`${prefix}/v1/perkstyles.json`),
  ])

  const perkMap: Record<number, StaticPerkData> = Object.fromEntries(perks.map(perk => [perk.id, {
    id: perk.id,
    name: perk.name,
    iconUrl: rewriteCdragonAsset(perk.iconPath, patch),
    shortDesc: perk.shortDesc,
    longDesc: perk.longDesc,
  }]))
  const perkStyleMap: Record<number, StaticPerkStyleData> = Object.fromEntries(perkStyles.styles.map(style => [style.id, {
    id: style.id,
    name: style.name,
    iconUrl: rewriteCdragonAsset(style.iconPath, patch),
    tooltip: style.tooltip?.trim() || undefined,
  }]))

  // Seven slots per style: 0 keystones, 1-3 the regular rows, 4-6 the stat
  // shards (the same for every style, so read once).
  const styles: RuneTreeStyle[] = perkStyles.styles.map(style => ({
    styleId: style.id,
    name: style.name,
    iconUrl: rewriteCdragonAsset(style.iconPath, patch),
    keystones: style.slots[0]?.perks ?? [],
    subRows: style.slots.slice(1, 4).map(slot => slot.perks),
  }))
  const first = perkStyles.styles[0]
  const shardSlots = first ? first.slots.slice(4, 7).map(slot => slot.perks) : []

  return { styles, perks: perkMap, perkStyles: perkStyleMap, shardSlots }
}

export interface ChampionDetailResponse {
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

/** `{{ abilityresourcename }}` in a cost type, resolved to the champion's resource. */
function resolveCostType(costType: string | undefined, partype: string): string | undefined {
  if (!costType) return costType
  const resolved = costType
    .replace(/\{\{\s*abilityresourcename\s*\}\}/gi, partype)
    .replace(/\(\(\s*abilityresourcename\s*\)\)/gi, partype)
  return resolved.trim() === '' ? undefined : resolved
}

const SPELL_SLOTS = ['Q', 'W', 'E', 'R'] as const

export function toChampionStatic(
  detail: ChampionDetailResponse,
  alias: string,
  name: string,
  patch: string,
): ChampionStaticData {
  const partype = detail.data[alias]?.partype ?? ''
  const championSpells: Record<string, StaticChampionSpellData> = Object.fromEntries(
    (detail.data[alias]?.spells ?? []).slice(0, SPELL_SLOTS.length).flatMap((spell, index) => {
      const key = SPELL_SLOTS[index]
      if (!key) return []
      return [[key, {
        key,
        name: spell.name,
        iconUrl: getChampionSpellImageUrl(spell.image.full, patch) ?? '',
        description: spell.description,
        cooldownBurn: spell.cooldownBurn,
        costBurn: spell.costBurn,
        costType: resolveCostType(spell.costType, partype),
        rangeBurn: spell.rangeBurn,
      }]]
    }),
  )
  return {
    championName: name,
    championIconUrl: `${DDRAGON}/${patch}/img/champion/${alias}.png`,
    championSpells,
    partype,
  }
}
