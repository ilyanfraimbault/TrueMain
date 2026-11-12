import type { DamageProfile, DamageProfilesResponse } from '~/utils/damage-profile'
import { indexProfiles, profileOf as lookup } from '~/utils/damage-profile'
import { apiGet } from '~/composables/useApi'
import { useSessionRead } from '~/composables/useSessionRead'

/**
 * Every champion's damage profile (#1905), fetched once per session: one
 * payload for the whole patch, so a team bar is computed here rather than
 * asked for per pick. It moves with the aggregation, not with a draft.
 */
export function useDamageProfiles() {
  const { data, status, load } = useSessionRead('damage-profiles', () => apiGet<DamageProfilesResponse>('/champions/damage-profiles'))
  const profiles = computed<DamageProfile[]>(() => data.value?.profiles ?? [])
  const patch = computed(() => data.value?.patch ?? null)

  const index = computed(() => indexProfiles(profiles.value))

  /** A champion on a lane, or its champion-wide entry; null when nothing is known. */
  const profileOf = (championId: number | null, position: string | null | undefined): DamageProfile | null =>
    championId === null ? null : lookup(index.value, championId, position)

  return { profiles, patch, status, load, profileOf }
}
