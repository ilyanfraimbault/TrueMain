import type { DraftState } from '~/types/lcu'
import { POSITIONS } from '#shared/utils/positions'
import { freeLane, withLane } from '~/utils/draft-lane'

/**
 * The lane the draft ranks for (#2461). The client's when it assigns one;
 * otherwise — blind pick, a custom game — the one the player chose, else the
 * one our allies leave free. Everything under the draft screen reads the draft
 * this returns, so a chosen lane behaves like an assigned one.
 */
export function useDraftLane(client: Ref<DraftState>) {
  /** The client gave us a lane: nothing to choose. */
  const assigned = computed(() => client.value.myPosition !== '')

  /** A lane can be chosen: none was assigned, and the queue is a five-lane one (not Arena). */
  const choosable = computed(() => !assigned.value && client.value.myTeam.length === POSITIONS.length)

  /** The player's own choice, held for this champion select. */
  const chosen = ref<string | null>(null)

  const lane = computed(() => {
    if (assigned.value) return client.value.myPosition
    if (!choosable.value) return ''
    return chosen.value ?? freeLane(client.value.myTeam) ?? ''
  })

  const draft = computed(() => withLane(client.value, lane.value))

  function choose(value: string | null) {
    if (choosable.value) chosen.value = value
  }

  return { draft, lane, choosable, choose }
}
