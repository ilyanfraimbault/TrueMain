import type { ProfileReadModel } from '#shared/contract/api'
import type { ChampionStaticListItem } from '~~/shared/types/static-data'

/**
 * Who a truemain's banner (`PagePlayerProfile`) draws: the player — their
 * identity, or before the profile lands the slug's Riot ID (`fallbackName`)
 * without a picture — and the champion they truemain, the dedication's
 * champion, else their first main, whose splash the profile's banner sits on.
 * Shared by the profile and the player-scoped champion page.
 */
export function useTruemainBanner(
  profile: MaybeRefOrGetter<ProfileReadModel | null | undefined>,
  champions: MaybeRefOrGetter<ChampionStaticListItem[] | null | undefined>,
  fallbackName: MaybeRefOrGetter<string>,
) {
  const truemainChampion = computed(() => {
    const value = toValue(profile)
    const id = value?.dedication?.championId ?? value?.mains[0]?.championId
    const champion = id ? toValue(champions)?.find(entry => entry.championId === id) : null
    return champion ? { name: champion.name, iconUrl: champion.iconUrl } : null
  })

  const identity = computed(() => toValue(profile)?.identity ?? { gameName: toValue(fallbackName), tagLine: null })

  return { truemainChampion, identity }
}
