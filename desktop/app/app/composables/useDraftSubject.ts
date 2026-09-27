import type { CompositionSlot } from '~/types/build'
import type { EnemyLane } from '~/types/draft'
import type { DraftState } from '~/types/lcu'
import type { BuildSubject } from '~/composables/useDraftBuild'

/** A champion clicked on the board to read its build. */
export interface ViewedPick {
  team: 'ally' | 'enemy'
  championId: number
}

/**
 * Whose build the draft shows, and the draft around it from that champion's
 * side: its team as allies, the other as enemies.
 *
 * In order: a champion clicked on the board, then a candidate clicked in the
 * suggestions (as if we had picked it), then our own pick — which counts while
 * it is only hovered, since that is the build being weighed. A clicked
 * champion that has left the draft (a lane re-solved, a pick changed) falls
 * back to us rather than to nothing.
 */
export function useDraftSubject(
  draft: Ref<DraftState>,
  enemyLanes: Ref<EnemyLane[]>,
  viewed: Ref<ViewedPick | null>,
  previewed: Ref<number | null>,
) {
  /** Locked picks of our side, on their lanes. */
  const allyPicks = computed<CompositionSlot[]>(() => draft.value.myTeam
    .filter(slot => slot.locked && slot.championId !== null && slot.position)
    .map(slot => ({ championId: slot.championId!, position: slot.position })))

  /** Their picks, on the lanes the guesser (and the user's corrections) put them. */
  const enemyPicks = computed<CompositionSlot[]>(() => enemyLanes.value
    .filter(lane => draft.value.enemyChampions.includes(lane.championId))
    .map(lane => ({ championId: lane.championId, position: lane.position })))

  const subject = computed<BuildSubject | null>(() => {
    const target = viewed.value
    if (target) {
      const own = target.team === 'ally' ? allyPicks.value : enemyPicks.value
      const other = target.team === 'ally' ? enemyPicks.value : allyPicks.value
      const self = own.find(pick => pick.championId === target.championId)
      if (self) {
        return {
          championId: self.championId,
          request: {
            position: self.position,
            allies: own.filter(pick => pick.championId !== self.championId),
            enemies: other,
          },
        }
      }
    }
    const position = draft.value.myPosition
    const me = previewed.value ?? draft.value.myChampion
    if (me === null || !position) return null
    return {
      championId: me,
      request: {
        position,
        allies: allyPicks.value.filter(pick => pick.position !== position),
        enemies: enemyPicks.value,
      },
    }
  })

  /** The champion the build is for, and the one it faces on its lane. */
  const duel = computed(() => {
    const current = subject.value
    if (!current) return { championId: draft.value.myChampion, opponentId: null, position: draft.value.myPosition || null }
    const position = current.request.position
    const opponentId = current.request.enemies.find(pick => pick.position === position)?.championId ?? null
    return { championId: current.championId, opponentId, position }
  })

  /** Which side the build on screen is for, in the player's words. */
  const whose = computed(() => {
    if (viewed.value && subject.value?.championId === viewed.value.championId) return viewed.value.team === 'ally' ? 'Ally' : 'Enemy'
    if (previewed.value !== null) return 'Preview'
    return draft.value.myChampionLocked ? 'Your pick' : 'Hovering'
  })

  return { subject, duel, whose }
}
