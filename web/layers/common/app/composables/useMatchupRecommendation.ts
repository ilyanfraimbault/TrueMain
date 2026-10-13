import type { CompositionBuildRequest, CompositionBuildResponse, CompositionSlotInput } from '#shared/types/composition'
import { describeStaleFetchError } from '#common/utils/errors'
import { POSITION_OPTIONS, type ChampionPosition } from '#common/utils/positions'

type DraftSlots = Record<ChampionPosition, number | null>

/**
 * Debounce window between the last draft edit and the refetch. Long enough to
 * swallow a burst of picks, short enough that the page still feels live.
 */
const REFETCH_DEBOUNCE_MS = 400

function toSlots(
  slots: DraftSlots,
  extra?: { position: ChampionPosition, championId: number } | null,
): CompositionSlotInput[] {
  const picks = POSITION_OPTIONS
    .filter(option => slots[option.value] !== null)
    .map(option => ({ position: option.value as string, championId: slots[option.value] as number }))
  return extra ? [...picks, extra] : picks
}

/**
 * The matchup page's live recommendation: every draft edit re-queries the
 * composition build after a short debounce, and the answer shown is only ever
 * one fetched for the matchup on screen.
 */
export function useMatchupRecommendation(draft: {
  playedChampionId: Ref<number | null>
  playedPosition: Ref<ChampionPosition | null>
  /**
   * The role opponent lives outside the enemy slot map: it is the matchup, the
   * one pick that hard-filters the sampled games. It is folded back into the
   * enemy slots only when the request is built.
   */
  opponentChampionId: Ref<number | null>
  allySlots: Ref<DraftSlots>
  enemySlots: Ref<DraftSlots>
}) {
  const { playedChampionId, playedPosition, opponentChampionId, allySlots, enemySlots } = draft

  const { data: response, isLoading, error: responseError, submit, clear } = useCompositionBuild()

  // What a recommendation is *for*: an ally or enemy pick refines it, a new
  // champion, role or opponent makes it another recommendation altogether.
  const matchupScope = computed(() =>
    `${playedChampionId.value}-${playedPosition.value}-${opponentChampionId.value}`)

  // The request the newest answer was fetched with, and the matchup it was for.
  const submitted = shallowRef<{ scope: string, request: CompositionBuildRequest } | null>(null)

  function submitDraft(championId: number, request: CompositionBuildRequest) {
    submitted.value = { scope: matchupScope.value, request }
    return submit(championId, request)
  }

  // An answer paired with its request — only while it is still for the matchup
  // on screen, so a late answer to a changed matchup is never kept as its own.
  const answer = computed<{ recommendation: CompositionBuildResponse, request: CompositionBuildRequest } | null>(() =>
    response.value && submitted.value?.scope === matchupScope.value
      ? { recommendation: response.value, request: submitted.value.request }
      : null)

  // A failed draft edit keeps the previous recommendation on screen and answers
  // with a toast (#1668); a failure with nothing to keep is the inline alert.
  const actionToast = useActionToast()
  const { data: shownAnswer, error, staleError } = useRefetchFallback({
    data: answer,
    status: computed(() => (isLoading.value ? 'pending' : responseError.value ? 'error' : 'success')),
    error: responseError,
  }, {
    scope: matchupScope,
    onStaleFailure: failure => actionToast.failure('Could not update the recommendation', describeStaleFetchError(failure)),
  })
  const recommendation = computed(() => shownAnswer.value?.recommendation ?? null)

  /**
   * The exact body the recommendation was (or is about to be) fetched with —
   * reused verbatim by the provenance drawer (#940), which re-lists that same
   * selection instead of re-deriving the draft.
   */
  const currentDraftRequest = computed<CompositionBuildRequest | null>(() => {
    if (playedChampionId.value === null || playedPosition.value === null) return null
    const position = playedPosition.value
    const opponent = opponentChampionId.value
    return {
      position,
      allies: toSlots(allySlots.value),
      enemies: toSlots(enemySlots.value, opponent === null ? null : { position, championId: opponent }),
    }
  })

  // The provenance drawer re-lists the games behind the recommendation *shown*.
  const shownDraftRequest = computed(() => shownAnswer.value?.request ?? currentDraftRequest.value)

  function retryDraft() {
    const request = currentDraftRequest.value
    if (playedChampionId.value !== null && request !== null) void submitDraft(playedChampionId.value, request)
  }

  let refetchTimer: ReturnType<typeof setTimeout> | undefined

  // Live mode: every draft edit re-queries after a short debounce — there is no
  // submit button. The panel and the stats strip fall back to their skeletons
  // while the next answer loads, rather than keeping the previous one dimmed
  // (the composable also drops out-of-order responses).
  watch(
    [playedChampionId, playedPosition, opponentChampionId, allySlots, enemySlots],
    () => {
      clearTimeout(refetchTimer)
      if (playedChampionId.value === null || playedPosition.value === null) {
        clear()
        return
      }
      const championId = playedChampionId.value
      // Capture the body alongside the champion id: the drawer and the backend's
      // 30 s cache both key on this exact object, so it is composed once and the
      // fetch and the provenance list cannot drift apart.
      const request = currentDraftRequest.value
      if (request === null) return
      refetchTimer = setTimeout(() => {
        void submitDraft(championId, request)
      }, REFETCH_DEBOUNCE_MS)
    },
    { deep: true, immediate: true },
  )

  onBeforeUnmount(() => clearTimeout(refetchTimer))

  return { isLoading, error, staleError, recommendation, shownDraftRequest, retryDraft }
}
