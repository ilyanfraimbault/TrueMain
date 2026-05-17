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

  async function setFilter(patch: string | null, position: ChampionPosition | null) {
    const nextPatch = patch ?? filters.value.patch
    const nextPosition = position ?? filters.value.position
    if (!nextPatch && !nextPosition) return

    await router.replace({
      query: {
        ...route.query,
        ...(nextPatch ? { patch: nextPatch } : {}),
        ...(nextPosition ? { position: nextPosition } : {}),
      },
    })
  }

  return { filters, hasFilters, setFilter }
}
