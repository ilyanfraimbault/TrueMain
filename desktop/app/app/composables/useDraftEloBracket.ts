import { draftEloBracket } from '~/utils/draft-elo'
import { readRankHistory } from '~/utils/lp-history'

/**
 * The bracket every draft request carries (#1906): the player's Solo/Duo tier
 * and above, as this machine last saw it — the record's standing once read this
 * session, else the rank history kept on the machine (`utils/lp-history`, the
 * tier the overlay's pace panel uses).
 */
export function useDraftEloBracket() {
  const { state } = useLcuState()
  const { rankHistory } = usePlayerRecord()
  return computed(() => {
    const riotId = state.value.riotId
    const latest = rankHistory.value.at(-1) ?? (riotId ? readRankHistory(riotId).at(-1) : undefined)
    return draftEloBracket(latest?.tier)
  })
}
