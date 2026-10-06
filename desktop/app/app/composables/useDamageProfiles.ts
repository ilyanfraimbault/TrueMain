import type { DamageProfile, DamageProfilesResponse } from '~/utils/damage-profile'
import { indexProfiles, profileOf as lookup } from '~/utils/damage-profile'

/** A failed load retries on its own, the wait doubling up to a ceiling — as the tier list does. */
const RETRY_FIRST_MS = 2_000
const RETRY_MAX_MS = 30_000
let retryTimer: ReturnType<typeof setTimeout> | undefined
let retryDelay = RETRY_FIRST_MS

/**
 * Every champion's damage profile (#1905), fetched once per session: one
 * payload for the whole patch, so a team bar is computed here rather than
 * asked for per pick. It moves with the aggregation, not with a draft.
 */
export function useDamageProfiles() {
  const profiles = useState<DamageProfile[]>('damage-profiles', () => [])
  const patch = useState<string | null>('damage-profiles-patch', () => null)
  const status = useState<'idle' | 'pending' | 'ready' | 'error'>('damage-profiles-status', () => 'idle')

  async function load() {
    if (status.value === 'pending' || status.value === 'ready') return
    status.value = 'pending'
    try {
      const answer = await apiGet<DamageProfilesResponse>('/champions/damage-profiles')
      profiles.value = answer.profiles
      patch.value = answer.patch
      status.value = 'ready'
      clearTimeout(retryTimer)
      retryTimer = undefined
      retryDelay = RETRY_FIRST_MS
    }
    catch {
      status.value = 'error'
      if (retryTimer === undefined) {
        retryTimer = setTimeout(() => {
          retryTimer = undefined
          void load()
        }, retryDelay)
        retryDelay = Math.min(retryDelay * 2, RETRY_MAX_MS)
      }
    }
  }
  void load()

  const index = computed(() => indexProfiles(profiles.value))

  /** A champion on a lane, or its champion-wide entry; null when nothing is known. */
  const profileOf = (championId: number | null, position: string | null | undefined): DamageProfile | null =>
    championId === null ? null : lookup(index.value, championId, position)

  return { profiles, patch, status, load, profileOf }
}
