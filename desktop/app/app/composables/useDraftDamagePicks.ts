import type { TeamRow } from '~/types/draft'
import type { DraftState } from '~/types/lcu'

/** The damage mix's picks (#1907), side by side, and our locked allies a suggested pick is read against. */
export function useDraftDamagePicks(
  draft: Ref<DraftState>,
  allyRows: Ref<TeamRow[]>,
  enemyRows: Ref<TeamRow[]>,
  /** Our own pick being weighed from a suggestion opened on the screen. */
  previewed: Ref<number | null>,
) {
  /**
   * Our side's picks for the damage bar: the locked ones, and our own pick being
   * weighed — hovered in the client, or a suggestion opened here — as a preview.
   * Other allies' hovers are not counted: they are not picks yet.
   */
  const allyDamagePicks = computed(() => {
    const picks: { championId: number, position: string | null, preview?: boolean }[] = allyRows.value
      .filter(row => row.championId !== null && row.locked && !(row.me && previewed.value !== null))
      .map(row => ({ championId: row.championId!, position: row.lane }))
    const mine = allyRows.value.find(row => row.me)
    const weighed = previewed.value ?? (mine && !mine.locked ? mine.championId : null)
    if (weighed !== null) picks.push({ championId: weighed, position: mine?.lane ?? (draft.value.myPosition || null), preview: true })
    return picks
  })

  /** Theirs, on the lanes the guesser (and the player's corrections) put them. */
  const enemyDamagePicks = computed(() => enemyRows.value
    .filter(row => row.championId !== null)
    .map(row => ({ championId: row.championId!, position: row.lane })))

  /** Our locked allies, ourselves excluded: what a suggested pick's damage note is read against. */
  const lockedAllies = computed(() => allyRows.value
    .filter(row => row.championId !== null && row.locked && !row.me)
    .map(row => ({ championId: row.championId!, position: row.lane })))

  return { allyDamagePicks, enemyDamagePicks, lockedAllies }
}
