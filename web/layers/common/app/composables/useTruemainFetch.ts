import type { WatchSource } from 'vue'
import type { RemoteRead } from '#common/utils/remote-read'
import { failed, isColdLoad, startLoading } from '#common/utils/remote-read'

interface UseTruemainFetchOptions<TResponse> {
  /**
   * Issue the request for the current reactive state. Called with the
   * resolved (non-empty) name tag; must let a failed response throw (no
   * `ignoreResponseError`) — the thrown status is what tells a 404 apart from
   * an outage — and forward `signal` so a superseded or abandoned request is
   * cancelled.
   */
  request: (nameTag: string, signal: AbortSignal) => Promise<TResponse | null>
  /**
   * Shape check on a successful response. A 2xx whose body fails it is read as
   * <c>notFound</c>, like a 404.
   */
  validate: (response: TResponse | null) => response is TResponse
  /** Store a validated response into the consumer's data ref(s). */
  onResponse: (response: TResponse) => void
  /** Clear the consumer's data ref(s) — empty name tag or not-found. */
  onClear: () => void
  /** Extra reactive inputs (beyond the name tag) that retrigger the fetch. */
  watch?: WatchSource[]
  /**
   * Gate on the request. While false nothing is issued and the bundle stays in
   * its initial-loading state — a consumer keeps rendering skeletons instead of
   * an "empty" answer nobody asked the API for. Flipping it to true runs the
   * fetch that mount would otherwise have run. Defaults to true.
   */
  enabled?: MaybeRefOrGetter<boolean>
}

/**
 * Shared skeleton for the truemain client-side fetch composables
 * (<c>useTruemainProfile</c> / <c>useTruemainRankHistory</c> /
 * <c>useTruemainMatches</c>): one {@link RemoteRead} phase (`state`) plus the
 * fetch lifecycle around it; `isLoading` / `isInitialLoading` / `notFound` /
 * `error` are read-only views of that phase (#2476). Deliberately hand-rolled instead of
 * `useAsyncData` — these fetches are client-only by design (private-ish, no
 * SSR cross-pollination between viewers), so there is no payload cache to
 * integrate with.
 *
 * The fetch runs once when the component mounts and refires when the name tag
 * (or any extra watch source) changes. An empty name tag short-circuits into
 * the cleared state so a parent page can bind to a still-empty ref without
 * triggering a 404 round trip on the first tick. `enabled` holds the request
 * back entirely — that is how the favorites page bounds its fan-out (#872).
 *
 * Responses are gated on a monotonic request token, the same guard
 * `useCompositionBuild` / `useCompositionBuildGames` / `useTruemainSearch`
 * carry: only the newest request may write the refs, so a slow older response
 * can never land under newer inputs.
 *
 * Must be called from a component `setup()`: on a server render or a
 * hydration the initial run hangs off `onMounted`, which is what makes
 * "client-only" true rather than merely intended (see the comment on that
 * call). On a client-side navigation it runs during setup instead, and `ready`
 * resolves once it has settled, so a page can await it (#1689).
 */
export function useTruemainFetch<TResponse>(
  nameTag: MaybeRefOrGetter<string>,
  options: UseTruemainFetchOptions<TResponse>,
) {
  const nameTagRef = computed(() => toValue(nameTag))
  const enabledRef = computed(() => toValue(options.enabled ?? true))

  // Shallow: the phase is replaced whole on every transition, and the answer it
  // carries is the consumer's to keep reactive (`onResponse`).
  const state = shallowRef<RemoteRead<TResponse>>({ kind: 'waiting' })
  const isLoading = computed(() => state.value.kind === 'loading')
  const isInitialLoading = computed(() => isColdLoad(state.value))
  const notFound = computed(() => state.value.kind === 'notFound')
  const error = computed(() => (state.value.kind === 'error' ? state.value.error : null))

  // Monotonic request token — the pattern `useCompositionBuild` and
  // `useTruemainSearch` already carry. Every consumer here refires on its own
  // inputs (`useTruemainMatches` on page, position and championId), and
  // `$fetch` resolutions are not ordered: on a slow link, stepping from page 3
  // to page 4 can let page 3's response land last and write its rows under a
  // pager reading 4. Bumped before *any* work — including the cleared path —
  // so clearing also invalidates whatever is still in flight.
  let requestSeq = 0
  // The newest request's controller. The token above only stops a stale
  // response from being *written*; aborting stops it from being *fetched* —
  // stepping through pages or leaving the profile otherwise leaves every
  // superseded request running to completion (#1712).
  let controller: AbortController | null = null

  function abortInFlight() {
    controller?.abort()
    controller = null
  }

  async function run(fresh: boolean) {
    // Gated: leave the phase untouched, so the consumer still reads
    // "initial loading" rather than a cleared — i.e. empty-looking — bundle.
    if (!enabledRef.value) return

    const seq = ++requestSeq
    abortInFlight()

    if (!nameTagRef.value) {
      options.onClear()
      state.value = { kind: 'cleared' }
      return
    }

    state.value = startLoading(state.value, { fresh })
    controller = new AbortController()
    try {
      const response = await options.request(nameTagRef.value, controller.signal)

      // Superseded while in flight: a newer request owns the phase now.
      if (seq !== requestSeq) return

      if (!options.validate(response)) {
        options.onClear()
        state.value = { kind: 'notFound' }
        return
      }

      options.onResponse(response)
      state.value = { kind: 'ready', data: response }
    }
    catch (err) {
      if (seq !== requestSeq) return
      const next = failed(state.value, err)
      if (next.kind === 'notFound') options.onClear()
      state.value = next
    }
  }

  /** Refetch, keeping the current answer on screen until the new one lands. */
  function execute() {
    return run(false)
  }

  /** Refetch from a cold state: the skeleton, not the previous answer, stands meanwhile. */
  function reload() {
    return run(true)
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
  //
  // A client-side navigation hydrates nothing, so the first request runs right
  // away and `ready` hands it to the page: a page that awaits it in setup keeps
  // the outgoing page on screen under the loading bar until the answer is in,
  // and opens on its data rather than its skeleton (#1689).
  const hydrating = !import.meta.client || useNuxtApp().isHydrating
  const ready = hydrating ? Promise.resolve() : execute()
  if (hydrating) onMounted(() => { void execute() })
  // `enabledRef` is watched too, so a gate opening after mount runs the fetch
  // mount skipped.
  watch([nameTagRef, enabledRef, ...(options.watch ?? [])], () => { void execute() })
  // Bumping the token first makes the aborted request's rejection a stale one,
  // so it never lands in `error`.
  onScopeDispose(() => {
    requestSeq++
    abortInFlight()
  })

  return {
    /** The phase itself, for a consumer that switches on it rather than on the flag views. */
    state: computed(() => state.value),
    isLoading,
    isInitialLoading,
    notFound,
    error,
    execute,
    reload,
    ready,
  }
}
