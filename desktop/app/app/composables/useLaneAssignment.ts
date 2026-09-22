import type { EnemyLane, Lane } from '~/types/draft'
import { LANES } from '~/types/draft'

/** One lane row, as the panel renders it. */
export interface LaneSlot {
  lane: Lane
  championId: number | null
  confidence: number
  pinned: boolean
}

/**
 * The editable lane assignment.
 *
 * The server owns the guess; this owns the user's corrections. A correction is
 * a **pin**, not just a value — it goes back to the solver as a hard constraint
 * so the remaining lanes re-solve around it. That is what lets one gesture fix
 * several wrong slots instead of one.
 */
export function useLaneAssignment(lanes: Ref<EnemyLane[]>) {
  /** Champion id → lane, for every slot the user set by hand. */
  const pinned = ref<Record<number, string>>({})

  /** The slot picked up by a click, awaiting its destination. */
  const selected = ref<Lane | null>(null)

  const slots = computed<LaneSlot[]>(() =>
    LANES.map((lane) => {
      const entry = lanes.value.find(l => l.position === lane)
      return {
        lane,
        championId: entry?.championId ?? null,
        confidence: entry?.confidence ?? 0,
        pinned: entry ? entry.championId in pinned.value : false,
      }
    }),
  )

  const hasCorrections = computed(() => Object.keys(pinned.value).length > 0)

  /**
   * Exchange two lanes.
   *
   * Always a swap, never a move: the five lanes are exclusive, so dropping a
   * champion onto an occupied lane has to displace its occupant somewhere
   * valid. Overwriting would leave the user to repair a state we just broke.
   *
   * Both champions are pinned, including the displaced one — the user has now
   * expressed an opinion about both slots, and leaving the displaced one free
   * would let the next re-solve undo half the correction.
   */
  function swap(from: Lane, to: Lane) {
    if (from === to) return

    const source = slots.value.find(s => s.lane === from)
    const target = slots.value.find(s => s.lane === to)
    if (!source) return

    const next = { ...pinned.value }
    if (source.championId !== null) next[source.championId] = to
    if (target?.championId != null) next[target.championId] = from
    pinned.value = next
    selected.value = null
  }

  /**
   * The click-then-click path, which performs the identical swap.
   *
   * Champion select is a thirty-second window under time pressure, and a
   * five-row drag on a trackpad is slower than two clicks. It is also what
   * makes the panel operable from the keyboard rather than being a drag-only
   * dead end.
   */
  function activate(lane: Lane) {
    if (selected.value === null) {
      const slot = slots.value.find(s => s.lane === lane)
      // Picking up an empty lane would let a user "select nothing" and then
      // wonder why the next click does nothing.
      if (slot?.championId == null) return
      selected.value = lane
      return
    }
    if (selected.value === lane) {
      selected.value = null
      return
    }
    swap(selected.value, lane)
  }

  function reset() {
    pinned.value = {}
    selected.value = null
  }

  return { slots, pinned, selected, hasCorrections, swap, activate, reset }
}
