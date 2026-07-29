import type { WatchSource } from 'vue'

interface UseTruemainFetchOptions<TResponse> {
  /**
   * Issue the request for the current reactive state. Called with the
   * resolved (non-empty) name tag; expected to pass `ignoreResponseError`
   * so a controller 404 resolves to a null body instead of throwing.
   */
  request: (nameTag: string) => Promise<TResponse | null>
  /**
   * Shape check on the raw response. `ignoreResponseError` turns a 404 into
   * a null body, so the only way to tell "not found" apart from "no body"
   * is a check on the payload shape — anything that fails it is surfaced as
   * <c>notFound</c>.
   */
  validate: (response: TResponse | null) => response is TResponse
  /** Store a validated response into the consumer's data ref(s). */
  onResponse: (response: TResponse) => void
  /** Clear the consumer's data ref(s) — empty name tag or not-found. */
  onClear: () => void
  /** Extra reactive inputs (beyond the name tag) that retrigger the fetch. */
  watch?: WatchSource[]
}

/**
 * Shared skeleton for the truemain client-side fetch composables
 * (<c>useTruemainProfile</c> / <c>useTruemainRankHistory</c> /
 * <c>useTruemainMatches</c>): the loading / notFound / error ref bundle plus
 * the fetch lifecycle around it. Deliberately hand-rolled refs instead of
 * `useAsyncData` — these fetches are client-only by design (private-ish, no
 * SSR cross-pollination between viewers), so there is no payload cache to
 * integrate with.
 *
 * The fetch runs once when the component mounts and refires when the name tag
 * (or any extra watch source) changes. An empty name tag short-circuits into
 * the cleared state so a parent page can bind to a still-empty ref without
 * triggering a 404 round trip on the first tick.
 *
 * Must be called from a component `setup()`: the initial run hangs off
 * `onMounted`, which is what makes "client-only" true rather than merely
 * intended (see the comment on that call).
 */
export function useTruemainFetch<TResponse>(
  nameTag: MaybeRefOrGetter<string>,
  options: UseTruemainFetchOptions<TResponse>,
) {
  const nameTagRef = computed(() => toValue(nameTag))

  const isLoading = ref(false)
  const isInitialLoading = ref(true)
  const notFound = ref(false)
  const error = ref<unknown>(null)

  async function execute() {
    if (!nameTagRef.value) {
      options.onClear()
      notFound.value = false
      isInitialLoading.value = false
      return
    }

    isLoading.value = true
    error.value = null
    try {
      const response = await options.request(nameTagRef.value)

      if (!options.validate(response)) {
        notFound.value = true
        options.onClear()
        return
      }

      notFound.value = false
      options.onResponse(response)
    }
    catch (err) {
      error.value = err
    }
    finally {
      isLoading.value = false
      isInitialLoading.value = false
    }
  }

  // The initial run is deliberately hung off `onMounted` rather than an
  // `immediate: true` watcher, and that distinction is the whole of #862.
  //
  // An immediate watcher also fires during SSR, so the request the doc block
  // above calls "client-only by design" was in fact issued on the server. It
  // then usually *won*: the page's render is meanwhile awaiting its two
  // SSR-enabled static lookups (`useStaticRuneTree` / `useStaticSummonerSpells`
  // — external DDragon/CDragon round trips, and summoner-spells resolves the
  // latest patch uncached on every request), which is far slower than a local
  // `/api/truemains/{nameTag}/*` hit. So the profile landed in the shared
  // server-rendered markup, while the client's first — hydration — render
  // always starts from `isInitialLoading = true`, i.e. the skeleton branch.
  // Vue therefore reconciled skeletons against fully-rendered content: dozens
  // of node/children mismatches, then `insertBefore: node is not a child of
  // this node`, then a crash on a null `component` inside the patch loop. The
  // renderer is dead at that point, so the page sits in its skeletons for good
  // — the reported hang. Client-side navigation was never affected because it
  // hydrates nothing.
  //
  // `onMounted` never runs during SSR, so the server render is deterministic
  // (always the loading state), hydration is exact, and the per-viewer payload
  // stays out of shared HTML — which is what the no-cross-viewer-SSR rule on
  // profiles asked for in the first place.
  onMounted(() => { void execute() })
  watch([nameTagRef, ...(options.watch ?? [])], () => { void execute() })

  return {
    isLoading,
    isInitialLoading,
    notFound,
    error,
    execute,
  }
}
