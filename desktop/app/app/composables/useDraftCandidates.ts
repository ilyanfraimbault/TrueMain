import type { DraftState } from '~/types/lcu'

/** How many of the player's own champions are ranked: their ten most played on the lane. */
const POOL_SIZE = 10

/**
 * The champions the draft screen asks the recommendation to rank, and the
 * player's pool behind them: the player's own champions on our lane by default,
 * every champion the tier list has on it once "My pool" is turned off.
 */
export function useDraftCandidates(draft: Ref<DraftState>) {
  const { laneEntries, entryOf } = useTierList()
  const { state: lcu } = useLcuState()

  /** The player's champions, most mastery first — empty until the client has said. */
  const championPool = computed(() => lcu.value.championPool ?? [])
  const hasPool = computed(() => championPool.value.length > 0)

  /** Rank the player's own champions, or every champion played on the lane. On by default when there is a pool. */
  const myPool = ref(true)

  /** Champions nobody can pick any more: banned, picked by the enemy, or locked or hovered by an ally. */
  const unavailable = computed(() => new Set([
    ...draft.value.allyBans,
    ...draft.value.enemyBans,
    ...draft.value.enemyChampions,
    ...draft.value.myTeam.filter(slot => !slot.isMe && slot.championId !== null).map(slot => slot.championId!),
  ]))

  /** The player's most-mastered champions played on our lane and still available, the first ten. */
  const lanePool = computed(() => {
    const position = draft.value.myPosition
    if (!position) return []
    return championPool.value.filter(id => !unavailable.value.has(id) && entryOf(id, position) !== null).slice(0, POOL_SIZE)
  })

  /**
   * What the endpoint ranks. Our pool: the player's most-mastered champions that
   * are played on our lane, the first ten still available. Otherwise every
   * champion the tier list has on the lane — split into several requests when
   * it is more than one can score.
   */
  const candidates = computed(() => {
    const position = draft.value.myPosition
    if (!position) return []
    if (myPool.value && hasPool.value) return lanePool.value
    return laneEntries(position).filter(entry => !unavailable.value.has(entry.championId)).map(entry => entry.championId)
  })

  return { myPool, hasPool, lanePool, candidates }
}
