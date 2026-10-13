import type { ProfileResponse } from '~~/shared/types/profile'
import type { ChampionStaticListItem } from '~~/shared/types/static-data'
import { getProfileIconUrl } from '~~/shared/utils/ddragon'

/**
 * What a truemain's banner (`PagePlayerProfile`) names besides the Riot ID: the
 * champion they truemain — the dedication's champion, else their first main —
 * and their profile icon. Shared by the profile and the player-scoped champion
 * page, so the two banners can never name a different truemain for one account.
 */
export function useTruemainBanner(
  profile: MaybeRefOrGetter<ProfileResponse | null | undefined>,
  champions: MaybeRefOrGetter<ChampionStaticListItem[] | null | undefined>,
  patch: MaybeRefOrGetter<string | null | undefined>,
) {
  const truemainChampion = computed(() => {
    const value = toValue(profile)
    const id = value?.dedication?.championId ?? value?.mains[0]?.championId
    const champion = id ? toValue(champions)?.find(entry => entry.championId === id) : null
    return champion ? { name: champion.name, iconUrl: champion.iconUrl } : null
  })

  const profileIconUrl = computed(() => {
    const identity = toValue(profile)?.identity
    return identity ? getProfileIconUrl(identity.profileIconId, toValue(patch) ?? null) : null
  })

  return { truemainChampion, profileIconUrl }
}
