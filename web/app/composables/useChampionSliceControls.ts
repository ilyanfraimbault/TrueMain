import type { ChampionPosition } from '#common/utils/positions'
import { ELO_BRACKET_ALL, eloBracketLabel, normalizeEloBracket } from '#common/utils/elo-brackets'
import type { ChampionStaticListItem } from '~~/shared/types/static-data'

type FilterState = ReturnType<typeof useChampionFilters>
type ChampionData = ReturnType<typeof useChampion>['data']

/**
 * The champion detail page's slice controls: the rank and matchup pickers'
 * bound values, the thin-sample / no-data-for-rank derivations, and the URL
 * reconciliation that drops a filter the API could not honour.
 */
export function useChampionSliceControls(options: {
  championId: ComputedRef<number>
  champion: ChampionData
  notEnoughData: Ref<boolean>
  filters: FilterState['filters']
  setFilter: FilterState['setFilter']
  staticList: Ref<ChampionStaticListItem[] | null | undefined>
  selectedPosition: Ref<string | null | undefined>
}) {
  const { championId, champion, notEnoughData, filters, setFilter, staticList, selectedPosition } = options

  // Elo filter (issue #526). Bind to the API-returned filter once available so
  // the rank select reflects what's actually shown; fall back to the URL filter
  // for the optimistic render before the fetch resolves.
  const selectedEloBracket = computed<string>(() =>
    normalizeEloBracket(champion.value?.eloBracket || filters.value.eloBracket),
  )

  // The elo filter forwarded to every live panel (matchups / scaling /
  // item-timings). Always a concrete bracket now that the page default is
  // Master+ rather than the server's ALL: a panel left to its own default would
  // quietly render every tier beside a header that says Master+.
  const eloBracketParam = computed(() => filters.value.eloBracket)

  // Matchup filter (#923): opponents are every champion but this one — there is no
  // matchup against yourself. The picker is hidden until the static list resolves,
  // since it searches by name.
  const opponentOptions = computed<ChampionStaticListItem[] | undefined>(() =>
    staticList.value?.filter(entry => entry.championId !== championId.value))

  // A matchup is scoped to a lane on the backend (the self-join matches both sides
  // on the position), so picking an opponent without one would 400. Pin the
  // position being displayed at the same time.
  async function onOpponentChange(value: number | null) {
    const position = selectedPosition.value ?? champion.value?.position ?? null
    await setFilter({
      opponentChampionId: value,
      ...(value && position ? { position: position as ChampionPosition } : {}),
    })
  }

  // A rank filter with no data: the fetch 404'd on a specific rank (the champion
  // may well have builds in other ranks). Distinct from the champion-level "no
  // data at all" state — here the page keeps the rank select so the user can
  // pick another rank instead of hitting a dead end.
  const noDataForRank = computed(() =>
    notEnoughData.value && selectedEloBracket.value !== ELO_BRACKET_ALL,
  )

  // Thin-sample qualifier, carried by the header's warning-triangle tooltip (the
  // idiom the retired-sample card and the builder panels already use) rather than
  // a full-width alert: it qualifies the numbers, it is not news.
  //
  // The one thing worth saying is that the sample is small — `minSampleMet` is
  // the API's own verdict on that (games >= ChampionsList:MinBuildSampleGames).
  // It deliberately no longer mentions how much of the all-rank population the
  // bracket covers: a reader deciding whether to trust this build cares that it
  // rests on 12 games, not that Master+ is 3% of everyone.
  //
  // `null` when there is nothing to qualify: the header keys the icon off it.
  const bracketNoticeText = computed<string | null>(() => {
    if (!champion.value || champion.value.minSampleMet) return null

    const games = champion.value.totalGames
    const countedGames = `${games} ${games === 1 ? 'game' : 'games'}`
    // Name the rank only when one is pinned: "Only 12 games in All ranks" is not
    // a sentence.
    const scope = selectedEloBracket.value === ELO_BRACKET_ALL
      ? countedGames
      : `${countedGames} in ${eloBracketLabel(selectedEloBracket.value)}`
    return `Only ${scope}, so this build isn't very representative.`
  })

  // When useChampion's 404 fallback drops the URL filters (no data for the
  // champion on that patch/position) the API returns the default slice, but the
  // dead patch/position query param lingers in the URL. Once the fetch resolves,
  // reconcile the URL with what was actually loaded so a no-data selection snaps
  // the address bar back to the initial state instead of pinning a stale filter.
  // The watch fires when champion data changes (never on the optimistic
  // stale-data phase) and once immediately on mount if champion is already
  // populated (e.g. an SSR payload) — so the dead filter is reconciled on the
  // first render too, not only on the next change. A *valid* selection — where
  // the API echoes the request — never triggers a reset.
  watch(champion, (data) => {
    if (!data) return
    // Only reset when the API actually returned a (truthy) value that differs:
    // a missing/empty patch or position in the response means "no slice info",
    // not "your valid filter was dropped", so it must never clear a live filter.
    const updates: { patch?: string | null, position?: ChampionPosition | null } = {}
    if (filters.value.patch && data.patch && filters.value.patch !== data.patch) updates.patch = null
    if (filters.value.position && data.position && filters.value.position !== data.position) updates.position = null
    if (updates.patch !== undefined || updates.position !== undefined) setFilter(updates).catch(console.error)
  }, { immediate: true })

  return {
    selectedEloBracket,
    eloBracketParam,
    opponentOptions,
    onOpponentChange,
    noDataForRank,
    bracketNoticeText,
  }
}
