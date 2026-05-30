import type { ChampionPosition } from '~/utils/positions'

function getQuery(value: string | string[] | undefined): string {
  return Array.isArray(value) ? value[0] ?? '' : value ?? ''
}

export function useChampionFilters() {
  const route = useRoute()
  const router = useRouter()

  const filters = computed(() => ({
    patch: getQuery(route.query.patch as string | string[] | undefined) || undefined,
    position: getQuery(route.query.position as string | string[] | undefined) || undefined,
  }))

  const hasFilters = computed(() => Boolean(filters.value.patch || filters.value.position))

  // `undefined` = leave the field alone, `null` = clear it, string = set it.
  // Mirrors the `applyFilterReset` pattern used on /champions so the two
  // pages handle filter clearing the same way.
  async function setFilter(updates: {
    patch?: string | null
    position?: ChampionPosition | null
  }) {
    const nextQuery: Record<string, string> = {}
    for (const [key, value] of Object.entries(route.query)) {
      if (typeof value === 'string') nextQuery[key] = value
    }

    if (updates.patch !== undefined) {
      if (updates.patch) nextQuery.patch = updates.patch
      else delete nextQuery.patch
    }
    if (updates.position !== undefined) {
      if (updates.position) nextQuery.position = updates.position
      else delete nextQuery.position
    }

    await router.replace({ query: nextQuery })
  }

  return { filters, hasFilters, setFilter }
}
