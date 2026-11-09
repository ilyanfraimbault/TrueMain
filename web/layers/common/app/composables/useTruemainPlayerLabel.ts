import type { ProfileResponse } from '#shared/types/profile'
import { riotIdLabel, truemainSlugLabel } from '#shared/utils/truemain-path'

/**
 * A player page's label, `gameName#tagLine`: the stored identity once the
 * profile has loaded, the route slug's own reading before (#948), so a server
 * render and a client navigation agree on what they print.
 */
export function useTruemainPlayerLabel(
  nameTag: MaybeRefOrGetter<string>,
  profile: MaybeRefOrGetter<ProfileResponse | null | undefined>,
) {
  return computed(() => {
    const identity = toValue(profile)?.identity
    return identity ? riotIdLabel(identity.gameName, identity.tagLine) : truemainSlugLabel(toValue(nameTag))
  })
}
