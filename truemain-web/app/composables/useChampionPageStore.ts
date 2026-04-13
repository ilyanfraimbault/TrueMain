import type { ChampionResponse } from '~/types/champions'
import type {
  ChampionStaticData,
  StaticChampionSpellData,
  StaticItemData,
  StaticSummonerSpellData
} from '~/types/static-data'
import {
  getChampionSpellImageUrl,
  getPositionIconUrl,
  getSummonerSpellImageUrl,
  normalizeDataDragonPatch
} from '~/utils/items'

export type ChampionPosition = 'TOP' | 'JUNGLE' | 'MIDDLE' | 'BOTTOM' | 'UTILITY'

const EMPTY_STATIC_DATA: ChampionStaticData = {
  championName: null,
  championIconUrl: null,
  items: {},
  summonerSpells: {},
  championSpells: {}
}

const POSITION_OPTIONS: Array<{ label: string, value: ChampionPosition, iconUrl: string }> = [
  { label: 'Top', value: 'TOP', iconUrl: getPositionIconUrl('TOP') },
  { label: 'Jungle', value: 'JUNGLE', iconUrl: getPositionIconUrl('JUNGLE') },
  { label: 'Middle', value: 'MIDDLE', iconUrl: getPositionIconUrl('MIDDLE') },
  { label: 'Bottom', value: 'BOTTOM', iconUrl: getPositionIconUrl('BOTTOM') },
  { label: 'Support', value: 'UTILITY', iconUrl: getPositionIconUrl('UTILITY') }
]

function getSingleQueryValue(value: string | string[] | undefined): string {
  return Array.isArray(value) ? value[0] ?? '' : value ?? ''
}

function toPositiveInteger(value: string, fallback: number): number {
  const parsed = Number.parseInt(value, 10)
  return Number.isFinite(parsed) && parsed > 0 ? parsed : fallback
}

async function fetchChampionData(
  apiBaseUrl: string,
  championId: number,
  query: {
    patch?: string
    position?: string
    maxDepth: number
    minBranchGames: number
  },
  fallbackRouteQuery: Ref<Record<string, string | undefined> | null>
) {
  try {
    fallbackRouteQuery.value = null

    return await $fetch<ChampionResponse>(`/champions/${championId}`, {
      baseURL: apiBaseUrl,
      query
    })
  }
  catch (error: unknown) {
    const statusCode = typeof error === 'object' && error !== null && 'statusCode' in error
      ? Number((error as { statusCode?: number }).statusCode)
      : undefined

    if (statusCode !== 404) {
      throw error
    }

    fallbackRouteQuery.value = {}

    return await $fetch<ChampionResponse>(`/champions/${championId}`, {
      baseURL: apiBaseUrl
    })
  }
}

async function fetchChampionStaticData(championId: number, patch: string | null): Promise<ChampionStaticData> {
  const normalizedPatch = normalizeDataDragonPatch(patch)
  if (!normalizedPatch) {
    return EMPTY_STATIC_DATA
  }

  const [itemDataResponse, summonerDataResponse, championListResponse] = await Promise.all([
    $fetch<{ data: Record<string, { name: string, image: { full: string }, gold: { total: number } }> }>(
      `https://ddragon.leagueoflegends.com/cdn/${normalizedPatch}/data/en_US/item.json`
    ),
    $fetch<{ data: Record<string, { key: string, name: string, image: { full: string } }> }>(
      `https://ddragon.leagueoflegends.com/cdn/${normalizedPatch}/data/en_US/summoner.json`
    ),
    $fetch<{ data: Record<string, { id: string, key: string, name: string, image: { full: string } }> }>(
      `https://ddragon.leagueoflegends.com/cdn/${normalizedPatch}/data/en_US/champion.json`
    )
  ])

  const items = Object.fromEntries(
    Object.entries(itemDataResponse.data).map(([itemId, item]) => [
      Number(itemId),
      {
        id: Number(itemId),
        name: item.name,
        iconUrl: `https://ddragon.leagueoflegends.com/cdn/${normalizedPatch}/img/item/${item.image.full}`,
        totalGold: item.gold.total
      } satisfies StaticItemData
    ])
  )

  const summonerSpells = Object.fromEntries(
    Object.values(summonerDataResponse.data).map((spell) => [
      Number(spell.key),
      {
        id: Number(spell.key),
        name: spell.name,
        iconUrl: getSummonerSpellImageUrl(spell.image.full, normalizedPatch) ?? ''
      } satisfies StaticSummonerSpellData
    ])
  )

  const championSummary = Object.values(championListResponse.data)
    .find((currentChampion) => Number(currentChampion.key) === championId)

  if (!championSummary) {
    return {
      ...EMPTY_STATIC_DATA,
      items,
      summonerSpells
    }
  }

  const championDetailResponse = await $fetch<{
    data: Record<string, { spells: Array<{ name: string, image: { full: string } }> }>
  }>(`https://ddragon.leagueoflegends.com/cdn/${normalizedPatch}/data/en_US/champion/${championSummary.id}.json`)

  const championDetail = championDetailResponse.data[championSummary.id]
  const slots = ['Q', 'W', 'E'] as const

  const championSpells = Object.fromEntries(
    (championDetail?.spells ?? []).slice(0, 3).flatMap((spell, index) => {
      const key = slots[index]
      if (!key) {
        return []
      }

      return [[
        key,
        {
          key,
          name: spell.name,
          iconUrl: getChampionSpellImageUrl(spell.image.full, normalizedPatch) ?? ''
        } satisfies StaticChampionSpellData
      ]]
    })
  )

  return {
    championName: championSummary.name,
    championIconUrl: `https://ddragon.leagueoflegends.com/cdn/${normalizedPatch}/img/champion/${championSummary.image.full}`,
    items,
    summonerSpells,
    championSpells
  }
}

async function fetchPatchCatalog(): Promise<string[]> {
  return await $fetch<string[]>('https://ddragon.leagueoflegends.com/api/versions.json')
}

export function useChampionPageStore(championId: ComputedRef<number>) {
  const route = useRoute()
  const router = useRouter()
  const runtimeConfig = useRuntimeConfig()
  const fallbackRouteQuery = ref<Record<string, string | undefined> | null>(null)

  const filters = reactive({
    patch: '',
    position: ''
  })

  const buildTreeQuery = computed(() => ({
    patch: getSingleQueryValue(route.query.patch as string | string[] | undefined) || undefined,
    position: getSingleQueryValue(route.query.position as string | string[] | undefined) || undefined,
    maxDepth: toPositiveInteger(getSingleQueryValue(route.query.maxDepth as string | string[] | undefined), 3),
    minBranchGames: toPositiveInteger(getSingleQueryValue(route.query.minBranchGames as string | string[] | undefined), 3)
  }))

  const championState = useAsyncData(
    () => `champion-${championId.value}-${JSON.stringify(buildTreeQuery.value)}`,
    () => fetchChampionData(runtimeConfig.public.apiBaseUrl, championId.value, buildTreeQuery.value, fallbackRouteQuery),
    {
      watch: [championId, buildTreeQuery]
    }
  )

  const champion = computed(() => championState.data.value ?? null)
  const summary = computed(() => champion.value?.summary ?? null)
  const core = computed(() => champion.value?.core ?? null)
  const advanced = computed(() => champion.value?.advanced ?? null)
  const buildTree = computed(() => champion.value?.buildTree ?? null)
  const itemPatch = computed(() =>
    buildTree.value?.patch || summary.value?.latestPatchVersion || buildTreeQuery.value.patch || null)

  const championStaticState = useAsyncData(
    () => `champion-static-${championId.value}-${itemPatch.value ?? 'none'}`,
    () => fetchChampionStaticData(championId.value, itemPatch.value),
    {
      server: false,
      watch: [itemPatch, championId]
    }
  )

  const championStatic = computed(() => championStaticState.data.value ?? EMPTY_STATIC_DATA)

  const patchCatalogState = useAsyncData(
    'ddragon-versions',
    fetchPatchCatalog,
    {
      server: false,
      default: () => []
    }
  )

  const patchOptions = computed(() => {
    const latestPatch = summary.value?.latestPatchVersion ?? buildTree.value?.patch ?? ''
    const selectedPatch = filters.patch
    const patches = new Set<string>(
      (patchCatalogState.data.value ?? [])
        .map((patch) => patch.split('.').slice(0, 2).join('.'))
        .filter(Boolean)
        .slice(0, 12)
    )

    if (selectedPatch) {
      patches.add(selectedPatch)
    }

    if (latestPatch) {
      patches.add(latestPatch)
    }

    return [...patches]
      .map(patch => ({
        label: patch,
        value: patch
      }))
      .sort((left, right) => right.value.localeCompare(left.value, undefined, { numeric: true }))
  })

  const isPageLoading = computed(() => championState.pending.value && !champion.value)
  const hasStaticData = computed(() =>
    championStatic.value.championName !== null ||
    Object.keys(championStatic.value.items).length > 0 ||
    Object.keys(championStatic.value.summonerSpells).length > 0 ||
    Object.keys(championStatic.value.championSpells).length > 0
  )
  const isStaticPending = computed(() => !hasStaticData.value)
  const effectivePosition = computed(() => filters.position || summary.value?.position || '')

  function syncFiltersFromRoute() {
    const routePatch = getSingleQueryValue(route.query.patch as string | string[] | undefined)
    const routePosition = getSingleQueryValue(route.query.position as string | string[] | undefined)

    filters.patch = routePatch || summary.value?.latestPatchVersion || filters.patch
    filters.position = routePosition || summary.value?.position || filters.position
  }

  async function applyFilters() {
    const patch = filters.patch || summary.value?.latestPatchVersion
    const position = filters.position || summary.value?.position

    if (!patch || !position) {
      return
    }

    if (
      getSingleQueryValue(route.query.patch as string | string[] | undefined) === patch &&
      getSingleQueryValue(route.query.position as string | string[] | undefined) === position
    ) {
      return
    }

    await router.replace({
      query: {
        patch,
        position,
        maxDepth: getSingleQueryValue(route.query.maxDepth as string | string[] | undefined) || undefined,
        minBranchGames: getSingleQueryValue(route.query.minBranchGames as string | string[] | undefined) || undefined
      }
    })
  }

  watch([
    () => route.fullPath,
    () => summary.value?.latestPatchVersion,
    () => summary.value?.position
  ], syncFiltersFromRoute, {
    immediate: true
  })

  watch([
    () => filters.patch,
    () => filters.position
  ], async () => {
    await applyFilters()
  }, {
    flush: 'post'
  })

  watch(fallbackRouteQuery, async (query) => {
    if (!query || import.meta.server) {
      return
    }

    const currentPatch = getSingleQueryValue(route.query.patch as string | string[] | undefined)
    const currentPosition = getSingleQueryValue(route.query.position as string | string[] | undefined)

    if (!currentPatch && !currentPosition) {
      fallbackRouteQuery.value = null
      return
    }

    await router.replace({ query })
    fallbackRouteQuery.value = null
  }, {
    flush: 'post'
  })

  function setPositionFilter(position: ChampionPosition) {
    filters.position = position
  }

  return {
    advanced,
    buildTree,
    champion,
    championState,
    championStatic,
    core,
    effectivePosition,
    filters,
    isPageLoading,
    isStaticPending,
    patchOptions,
    positionOptions: POSITION_OPTIONS,
    setPositionFilter,
    summary
  }
}
