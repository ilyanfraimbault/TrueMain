import type { ChampionResponse } from '~/types/champions'
import type {
  ChampionStaticData,
  StaticChampionSpellData,
  StaticItemData,
  StaticPerkData,
  StaticPerkStyleData,
  StaticSummonerSpellData
} from '~/types/static-data'
import {
  getChampionSpellImageUrl,
  getPositionIconUrl,
  getSummonerSpellImageUrl,
  normalizeDataDragonPatch
} from '~/utils/items'

const COMMUNITY_DRAGON_PREFIX
  = 'https://raw.communitydragon.org/latest/plugins/rcp-be-lol-game-data/global/default'

export type ChampionPosition = 'TOP' | 'JUNGLE' | 'MIDDLE' | 'BOTTOM' | 'UTILITY'

export const EMPTY_STATIC_DATA: ChampionStaticData = {
  championName: null,
  championIconUrl: null,
  items: {},
  summonerSpells: {},
  championSpells: {},
  perks: {},
  perkStyles: {}
}

export const POSITION_OPTIONS: Array<{ label: string, value: ChampionPosition, iconUrl: string }> = [
  { label: 'Top', value: 'TOP', iconUrl: getPositionIconUrl('TOP') },
  { label: 'Jungle', value: 'JUNGLE', iconUrl: getPositionIconUrl('JUNGLE') },
  { label: 'Middle', value: 'MIDDLE', iconUrl: getPositionIconUrl('MIDDLE') },
  { label: 'Bottom', value: 'BOTTOM', iconUrl: getPositionIconUrl('BOTTOM') },
  { label: 'Support', value: 'UTILITY', iconUrl: getPositionIconUrl('UTILITY') }
]

type ItemDataResponse = { data: Record<string, { name: string, image: { full: string }, gold: { total: number } }> }
type SummonerDataResponse = { data: Record<string, { key: string, name: string, image: { full: string } }> }
type ChampionListResponse = { data: Record<string, { id: string, key: string, name: string, image: { full: string } }> }
type PerksResponse = Array<{ id: number, name: string, iconPath: string }>
type PerkStylesResponse = { styles: Array<{ id: number, name: string, iconPath: string }> }
type ChampionDetailResponse = { data: Record<string, { spells: Array<{ name: string, image: { full: string } }> }> }

function getQuery(value: string | string[] | undefined): string {
  return Array.isArray(value) ? value[0] ?? '' : value ?? ''
}

function rewriteCdragonAsset(iconPath: string): string {
  if (!iconPath) return ''
  return iconPath.toLowerCase().replace(/^\/lol-game-data\/assets/, COMMUNITY_DRAGON_PREFIX)
}

const staticDataCache = new Map<string, Promise<ChampionStaticData>>()

async function loadStaticData(championId: number, patch: string): Promise<ChampionStaticData> {
  const [items, spells, champs, perks, perkStyles] = await Promise.all([
    $fetch<ItemDataResponse>(`https://ddragon.leagueoflegends.com/cdn/${patch}/data/en_US/item.json`).catch(() => ({ data: {} })),
    $fetch<SummonerDataResponse>(`https://ddragon.leagueoflegends.com/cdn/${patch}/data/en_US/summoner.json`).catch(() => ({ data: {} })),
    $fetch<ChampionListResponse>(`https://ddragon.leagueoflegends.com/cdn/${patch}/data/en_US/champion.json`).catch(() => ({ data: {} })),
    $fetch<PerksResponse>(`${COMMUNITY_DRAGON_PREFIX}/v1/perks.json`).catch(() => [] as PerksResponse),
    $fetch<PerkStylesResponse>(`${COMMUNITY_DRAGON_PREFIX}/v1/perkstyles.json`).catch(() => ({ styles: [] }))
  ])

  const itemMap: Record<number, StaticItemData> = Object.fromEntries(
    Object.entries(items.data).map(([id, item]) => [
      Number(id),
      {
        id: Number(id),
        name: item.name,
        iconUrl: `https://ddragon.leagueoflegends.com/cdn/${patch}/img/item/${item.image.full}`,
        totalGold: item.gold.total
      }
    ])
  )

  const summonerMap: Record<number, StaticSummonerSpellData> = Object.fromEntries(
    Object.values(spells.data).map((spell) => [
      Number(spell.key),
      {
        id: Number(spell.key),
        name: spell.name,
        iconUrl: getSummonerSpellImageUrl(spell.image.full, patch) ?? ''
      }
    ])
  )

  const perkMap: Record<number, StaticPerkData> = Object.fromEntries(
    perks.map(perk => [perk.id, { id: perk.id, name: perk.name, iconUrl: rewriteCdragonAsset(perk.iconPath) }])
  )

  const perkStyleMap: Record<number, StaticPerkStyleData> = Object.fromEntries(
    (perkStyles.styles ?? []).map(style => [style.id, { id: style.id, name: style.name, iconUrl: rewriteCdragonAsset(style.iconPath) }])
  )

  const summary = Object.values(champs.data).find(c => Number(c.key) === championId)
  if (!summary) {
    return { ...EMPTY_STATIC_DATA, items: itemMap, summonerSpells: summonerMap, perks: perkMap, perkStyles: perkStyleMap }
  }

  const detail = await $fetch<ChampionDetailResponse>(
    `https://ddragon.leagueoflegends.com/cdn/${patch}/data/en_US/champion/${summary.id}.json`
  ).catch((): ChampionDetailResponse => ({ data: {} }))

  const slots = ['Q', 'W', 'E'] as const
  const championSpells: Record<string, StaticChampionSpellData> = Object.fromEntries(
    (detail.data[summary.id]?.spells ?? []).slice(0, 3).flatMap((spell, index) => {
      const key = slots[index]
      if (!key) return []
      return [[key, { key, name: spell.name, iconUrl: getChampionSpellImageUrl(spell.image.full, patch) ?? '' }]]
    })
  )

  return {
    championName: summary.name,
    championIconUrl: `https://ddragon.leagueoflegends.com/cdn/${patch}/img/champion/${summary.image.full}`,
    items: itemMap,
    summonerSpells: summonerMap,
    championSpells,
    perks: perkMap,
    perkStyles: perkStyleMap
  }
}

function fetchStaticData(championId: number, patch: string | null): Promise<ChampionStaticData> {
  const normalized = normalizeDataDragonPatch(patch)
  if (!normalized) return Promise.resolve(EMPTY_STATIC_DATA)

  const key = `${championId}-${normalized}`
  const cached = staticDataCache.get(key)
  if (cached) return cached

  const promise = loadStaticData(championId, normalized).catch((error) => {
    staticDataCache.delete(key)
    throw error
  })
  staticDataCache.set(key, promise)
  return promise
}

export function useChampionPageStore(championId: ComputedRef<number>) {
  const route = useRoute()
  const router = useRouter()

  const query = computed(() => ({
    patch: getQuery(route.query.patch as string | string[] | undefined) || undefined,
    position: getQuery(route.query.position as string | string[] | undefined) || undefined,
    platformId: getQuery(route.query.platformId as string | string[] | undefined) || undefined,
    riotAccountId: getQuery(route.query.riotAccountId as string | string[] | undefined) || undefined,
    buildId: getQuery(route.query.buildId as string | string[] | undefined) || undefined,
    maxDepth: 7,
    minBranchGames: 1
  }))

  function fetchChampion(filtered: boolean): Promise<ChampionResponse> {
    const q = filtered
      ? query.value
      : { maxDepth: query.value.maxDepth, minBranchGames: query.value.minBranchGames }
    return $fetch<ChampionResponse>(`/api/champions/${championId.value}`, { query: q })
  }

  const championState = useAsyncData<ChampionResponse>(
    () => [
      'champion',
      championId.value,
      query.value.patch ?? '',
      query.value.position ?? '',
      query.value.platformId ?? '',
      query.value.riotAccountId ?? '',
      query.value.buildId ?? ''
    ].join('-'),
    async () => {
      try {
        return await fetchChampion(true)
      } catch (error: unknown) {
        const status = (error as { statusCode?: number }).statusCode
        const hasFilters = Boolean(
          query.value.patch || query.value.position || query.value.platformId
          || query.value.riotAccountId || query.value.buildId
        )
        // 404 with filters likely means "no data for that filter combo".
        // Fall back to the unfiltered champion so the page still renders
        // basic info instead of surfacing a hard error.
        if (status === 404 && hasFilters) {
          return await fetchChampion(false)
        }
        throw error
      }
    },
    { watch: [championId, query] }
  )

  const champion = computed(() => championState.data.value ?? null)
  const summary = computed(() => champion.value?.summary ?? null)
  const core = computed(() => champion.value?.core ?? null)
  const advanced = computed(() => champion.value?.advanced ?? null)
  const buildTree = computed(() => champion.value?.buildTree ?? null)

  const activePatch = computed(() =>
    buildTree.value?.patch || summary.value?.latestPatchVersion || query.value.patch || null)

  const staticState = useAsyncData(
    () => `champion-static-${championId.value}-${activePatch.value ?? 'none'}`,
    () => fetchStaticData(championId.value, activePatch.value),
    { server: false, watch: [activePatch, championId] }
  )

  const championStatic = computed(() => staticState.data.value ?? EMPTY_STATIC_DATA)

  const versionsState = useAsyncData(
    'ddragon-versions',
    () => $fetch<string[]>('https://ddragon.leagueoflegends.com/api/versions.json'),
    { server: false, default: () => [] }
  )

  const patchOptions = computed(() => {
    const seen = new Set<string>(
      (versionsState.data.value ?? [])
        .map(p => p.split('.').slice(0, 2).join('.'))
        .filter(Boolean)
        .slice(0, 12)
    )
    if (summary.value?.latestPatchVersion) seen.add(summary.value.latestPatchVersion)
    if (query.value.patch) seen.add(query.value.patch)
    return [...seen]
      .map(p => ({ label: p, value: p }))
      .sort((a, b) => b.value.localeCompare(a.value, undefined, { numeric: true }))
  })

  const selectedPatch = computed(() => query.value.patch || summary.value?.latestPatchVersion || '')
  const selectedPosition = computed<ChampionPosition | ''>(() => {
    const value = query.value.position || summary.value?.position || ''
    return POSITION_OPTIONS.some(o => o.value === value) ? value as ChampionPosition : ''
  })

  const isLoading = computed(() => championState.pending.value && !champion.value)

  async function setFilter(patch: string | null, position: ChampionPosition | null) {
    const nextPatch = patch ?? query.value.patch ?? summary.value?.latestPatchVersion
    const nextPosition = position ?? query.value.position ?? summary.value?.position
    if (!nextPatch || !nextPosition) return

    await router.replace({
      query: {
        ...route.query,
        patch: nextPatch,
        position: nextPosition
      }
    })
  }

  return {
    advanced,
    buildTree,
    championState,
    championStatic,
    core,
    isLoading,
    patchOptions,
    positionOptions: POSITION_OPTIONS,
    selectedPatch,
    selectedPosition,
    setFilter,
    summary
  }
}
