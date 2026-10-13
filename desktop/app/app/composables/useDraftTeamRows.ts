import type { DraftRecommendation, Lane, TeamRow } from '~/types/draft'
import type { DraftState } from '~/types/lcu'
import type { LaneSlot } from '~/composables/useLaneAssignment'
import { LANES } from '~/types/draft'

/** The two teams of the draft board, one row per card. */
export function useDraftTeamRows(
  draft: Ref<DraftState>,
  recommendation: Ref<DraftRecommendation | null>,
  slots: Ref<LaneSlot[]>,
) {
  /** Rows follow the lanes when the queue assigns them, so row n is a matchup. */
  const byLane = computed(() => draft.value.myPosition !== '')

  const allyRows = computed<TeamRow[]>(() => {
    const team = draft.value.myTeam
    const cells = byLane.value
      ? LANES.map(lane => team.find(slot => slot.position === lane) ?? null)
      : Array.from({ length: 5 }, (_, index) => team[index] ?? null)
    return cells.map((slot, index) => ({
      championId: slot?.championId ?? null,
      lane: (slot?.position || (byLane.value ? LANES[index] : null)) as Lane | null,
      locked: slot?.locked ?? false,
      me: slot?.isMe ?? false,
    }))
  })

  /**
   * With a lane answer the enemies follow the lanes and can be corrected by
   * drag; without one — early draft, or a queue with no lanes — they are listed
   * in the order they picked, and only an empty board carries the lanes.
   */
  const enemyRows = computed<TeamRow[]>(() => {
    if (recommendation.value) {
      return slots.value.map(slot => ({
        championId: slot.championId !== null && draft.value.enemyChampions.includes(slot.championId) ? slot.championId : null,
        lane: slot.lane,
        locked: true,
      }))
    }
    const open = byLane.value && draft.value.enemyChampions.length === 0
    return Array.from({ length: 5 }, (_, index) => ({
      championId: draft.value.enemyChampions[index] ?? null,
      lane: (open ? LANES[index] ?? null : null) as Lane | null,
      locked: true,
    }))
  })

  return { byLane, allyRows, enemyRows }
}
