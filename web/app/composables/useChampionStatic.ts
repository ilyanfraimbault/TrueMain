import type { ChampionStaticData } from '~~/shared/types/static-data'

const EMPTY_STATIC_DATA: ChampionStaticData = {
  championName: null,
  championIconUrl: null,
  championSpells: {},
  partype: '',
}

export function useChampionStatic(
  championId: MaybeRefOrGetter<number>,
  patch: MaybeRefOrGetter<string | null>,
) {
  const nuxtApp = useNuxtApp()
  // Single source for the cache key (same gesture as `useStaticFetch`): the key
  // `markStaticFetched` stamps must be exactly the one `getCachedData` reads
  // back, or the TTL entry lands under a key nothing ever looks up.
  const keyRef = computed(() => `champion-static-${toValue(championId)}-${toValue(patch) || 'none'}`)

  return useLazyAsyncData<ChampionStaticData>(
    () => keyRef.value,
    async () => {
      const id = toValue(championId)
      const resolvedPatch = toValue(patch) ?? ''
      const key = keyRef.value
      const data = await $fetch<ChampionStaticData>(`/api/static/${id}`, {
        query: { patch: resolvedPatch },
      })
      markStaticFetched(key, nuxtApp)
      return data
    },
    {
      default: () => EMPTY_STATIC_DATA,
      getCachedData: key => getStaticCachedData(key, nuxtApp),
      watch: [() => toValue(championId), () => toValue(patch)],
      server: false,
    },
  )
}
