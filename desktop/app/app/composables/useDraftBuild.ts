import type {
  BuildCoreView,
  BuildTreeNode,
  ChampionBuildResponse,
  CompositionBuildRequest,
  CompositionBuildResponse,
  CompositionLane,
} from '~/types/build'

/** Whose build to show: a champion on its lane, and the draft around it from its side. */
export interface BuildSubject {
  championId: number
  request: CompositionBuildRequest
}

/** What the panel draws: one build, the tree behind it, and how its lane goes. */
export interface DraftBuild {
  /** The champion this build is for — the panel may be showing someone else's. */
  championId: number
  core: BuildCoreView
  firstItemId: number
  buildTree: BuildTreeNode[]
  games: number
  wins: number
  /** The lane at 15 over the sampled games; null on the standard fallback. */
  lane: CompositionLane | null
  /** `draft` — computed for this composition; `standard` — the lane build, the matchup never having been recorded. */
  source: 'draft' | 'standard'
}

/**
 * Picks landing in quick succession — a double lock, the lane answer following
 * an enemy pick — collapse into one request instead of one each.
 */
const DEBOUNCE_MS = 150

/** What Rust answers for a request a newer one replaced. */
const SUPERSEDED = 'superseded'

/**
 * The build for one champion against the draft as it stands — the endpoint
 * behind the site's matchup page, fed every locked pick of both teams.
 *
 * It re-asks whenever the subject changes: a lock, a lane correction, another
 * champion clicked. A change landing while a request is out cancels that
 * request — aborted in Rust, or through the fetch's signal in a browser — and
 * asks again: the panel never waits on an answer to a draft that no longer
 * exists.
 */
export function useDraftBuild(subject: Ref<BuildSubject | null>) {
  const build = ref<DraftBuild | null>(null)
  const pending = ref(false)
  const error = ref<string | null>(null)

  /**
   * Watched as a string: the draft is replaced on every client push, the timer
   * ticking included, and only a change in the picks is a reason to ask again.
   */
  const key = computed(() => JSON.stringify(subject.value))

  let latest = 0
  let controller: AbortController | null = null
  let timer: ReturnType<typeof setTimeout> | undefined

  async function post(champion: number, body: CompositionBuildRequest, signal: AbortSignal) {
    if (insideTauri()) {
      const { invoke } = await import('@tauri-apps/api/core')
      return await invoke<CompositionBuildResponse>('composition_build', { championId: champion, request: body })
    }
    // `npm run dev` in a browser: the dev server proxies `/api` (nuxt.config).
    return await $fetch<CompositionBuildResponse>(`/api/champions/${champion}/composition-build`, {
      method: 'POST',
      body,
      signal,
    })
  }

  async function standard(champion: number, position: string, signal: AbortSignal): Promise<DraftBuild | null> {
    const answer = insideTauri()
      ? await (await import('@tauri-apps/api/core')).invoke<ChampionBuildResponse>('champion_build', {
          championId: champion,
          position,
          opponentChampionId: null,
        })
      : await $fetch<ChampionBuildResponse>(`/api/champions/${champion}`, { query: { position }, signal })
    const first = answer.builds[0]
    if (!first) return null
    return {
      championId: champion,
      core: first.core,
      firstItemId: first.firstItemId,
      buildTree: first.buildTree,
      games: first.games,
      wins: Math.round(first.games * first.winRate),
      lane: null,
      source: 'standard',
    }
  }

  async function load({ championId, request }: BuildSubject) {
    const id = ++latest
    controller = new AbortController()
    const { signal } = controller

    pending.value = true
    error.value = null
    try {
      const answer = await post(championId, request, signal)
      let next: DraftBuild | null
      if (answer.matchupRequested && !answer.matchupFound) {
        // The lane opponent was never recorded against this champion: the lane
        // build is the only answer there is, and the panel says it is that one.
        next = await standard(championId, request.position, signal)
      }
      else {
        const { corePath, firstItemId, buildTree, gamesConsidered, wins, ...rest } = answer.build
        next = {
          championId,
          core: { ...rest, itemPath: corePath },
          firstItemId,
          buildTree,
          games: gamesConsidered,
          wins,
          lane: answer.lane,
          source: 'draft',
        }
      }
      if (id === latest) build.value = next
    }
    catch (cause) {
      if (id !== latest || String(cause).includes(SUPERSEDED) || signal.aborted) return
      error.value = String(cause)
    }
    finally {
      if (id === latest) pending.value = false
    }
  }

  watch(
    key,
    () => {
      // The request out is for a draft that just stopped existing: drop it now,
      // not when the next one starts, or its answer could land in between.
      clearTimeout(timer)
      controller?.abort()
      latest++
      const next = subject.value
      if (next === null) {
        build.value = null
        pending.value = false
        return
      }
      pending.value = true
      // Another champion makes the build on screen wrong, not just stale.
      if (next.championId !== build.value?.championId) build.value = null
      timer = setTimeout(() => load(next), DEBOUNCE_MS)
    },
    { immediate: true },
  )

  onScopeDispose(() => {
    clearTimeout(timer)
    controller?.abort()
  })

  return { build, pending, error }
}
