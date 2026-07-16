import { isChampionPosition, type ChampionPosition } from '~/utils/positions'
import { firstParamValue } from '~/utils/route-params'

/**
 * 1-indexed current page sourced from `?page=` — so back/forward + direct
 * links stay in sync with the list state — plus its setter. Non-numeric or
 * <1 values coerce to 1 (the same clamping the backend does for safety, but
 * doing it here keeps the URL stable while the page mounts). `setPage`
 * strips `?page=` when landing back on page 1, keeping the URL identical to
 * the natural landing state instead of carrying a redundant `?page=1`.
 */
export function useRoutePage() {
  const route = useRoute()
  const router = useRouter()

  const currentPage = computed<number>(() => {
    const raw = firstParamValue(route.query.page)
    const parsed = Number.parseInt(raw ?? '', 10)
    return Number.isFinite(parsed) && parsed >= 1 ? parsed : 1
  })

  async function setPage(next: number) {
    const clamped = Math.max(1, Math.floor(next))
    if (clamped === currentPage.value) return
    const nextQuery = { ...route.query }
    if (clamped === 1) delete nextQuery.page
    else nextQuery.page = String(clamped)
    await router.replace({ query: nextQuery })
  }

  return { currentPage, setPage }
}

/**
 * Champion filter sourced from `?championId=` so deep links and back/forward
 * keep the selection. Invalid or non-positive values fall back to "no
 * filter" instead of reflecting attacker-controlled query params back
 * through the API.
 */
export function useRouteQueryChampionId() {
  const route = useRoute()
  return computed<number | null>(() => {
    const raw = firstParamValue(route.query.championId)
    const parsed = Number.parseInt(raw ?? '', 10)
    return Number.isFinite(parsed) && parsed > 0 ? parsed : null
  })
}

/**
 * Position filter sourced from `?position=` (Riot uppercase enum —
 * TOP/JUNGLE/...). The guard rejects garbage values so an
 * attacker-controlled query never reaches the API.
 */
export function useRouteQueryPosition() {
  const route = useRoute()
  return computed<ChampionPosition | null>(() => {
    const raw = firstParamValue(route.query.position)
    return isChampionPosition(raw) ? raw : null
  })
}

/**
 * Setter for a single query-string filter on a paginated list page. Setting
 * or clearing a filter always drops `?page=` — staying on, say, page 5 after
 * narrowing the filter risks landing past the new total. Pass `null` (or an
 * empty value) to clear the param.
 */
export function useRouteFilterSetter() {
  const route = useRoute()
  const router = useRouter()

  return async function setQueryFilter(name: string, value: string | null) {
    const nextQuery = { ...route.query }
    if (value) nextQuery[name] = value
    else delete nextQuery[name]
    delete nextQuery.page
    await router.replace({ query: nextQuery })
  }
}
