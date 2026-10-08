import type { ChampionDirectoryResponse } from '#shared/types/champion-directory'

/**
 * The tier a champion holds on one lane of a slice, for the champion page's
 * banner: the directory's own row for it (`GET /champions/directory` narrowed
 * to the champion and lane, #1734), so the letter is the one the tier list and
 * the champion list show for the same filters. Null until it lands, and when
 * the lane holds no tiered row. Client-only, like the directory itself; a
 * failure leaves the banner without a tier rather than raising anything.
 */
export function useChampionTier(options: {
  championId: MaybeRefOrGetter<number>
  position: MaybeRefOrGetter<string | null | undefined>
  patch: MaybeRefOrGetter<string | null | undefined>
  eloBracket: MaybeRefOrGetter<string | null | undefined>
  truemainsOnly: MaybeRefOrGetter<boolean>
}) {
  const query = computed(() => {
    const query: Record<string, string | number> = { pageSize: 1, championId: toValue(options.championId) }
    const position = toValue(options.position)
    if (position) query.position = position
    const patch = toValue(options.patch)
    if (patch) query.patch = patch
    const elo = toValue(options.eloBracket)
    if (elo) query.eloBracket = elo
    if (!toValue(options.truemainsOnly)) query.truemainsOnly = 'false'
    return query
  })

  const apiFetch = useApiFetch()
  const { data } = useAsyncData<ChampionDirectoryResponse | null>(
    () => `champion-tier-${Object.entries(query.value).map(([key, value]) => `${key}=${value}`).join('&')}`,
    (_nuxtApp, { signal }) => {
      const championId = Number(query.value.championId)
      if (!Number.isFinite(championId) || championId <= 0 || !query.value.position) return Promise.resolve(null)
      return apiFetch<ChampionDirectoryResponse>('/champions/directory', { query: query.value, signal }).catch(() => null)
    },
    { server: false, watch: [query], default: () => null },
  )

  return computed(() => data.value?.rows[0]?.tier ?? null)
}
