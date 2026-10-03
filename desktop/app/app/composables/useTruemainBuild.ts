import type { ChampionBuildResponse } from '~/types/build'

/** A true main's builds on one champion: `undefined` while unasked, `null` when they have none to show. */
type Answer = ChampionBuildResponse | null

/**
 * One true main's own build on a champion — the site's player-scoped champion
 * endpoint (`/truemains/{nameTag}/champions/{id}`), which picks the player's
 * most recent patch with enough games. Asked on the lane the view is for
 * first; a main who never played the champion there is asked across lanes, so
 * a click on them still shows what they run. Answers are kept for the session.
 */
export function useTruemainBuild() {
  const cache = useState<Record<string, Answer>>('truemain-builds', () => ({}))
  const pending = useState<Record<string, boolean>>('truemain-builds-pending', () => ({}))
  /** Asks that failed for another reason than "no build": not kept, so the next click asks again. */
  const failed = useState<Record<string, boolean>>('truemain-builds-failed', () => ({}))

  const keyOf = (nameTag: string, championId: number, position: string) => `${nameTag}:${championId}:${position}`

  const notFound = (cause: unknown) =>
    (cause as { statusCode?: number } | null)?.statusCode === 404 || /answered 404 /.test(String(cause))

  async function ask(nameTag: string, championId: number, position: string | null) {
    return await apiGet<ChampionBuildResponse>(`/truemains/${encodeURIComponent(nameTag)}/champions/${championId}`, { position })
  }

  /** On this lane, else across lanes; `null` when neither has a build, thrown when the ask itself failed. */
  async function resolve(nameTag: string, championId: number, position: string): Promise<Answer> {
    try {
      return await ask(nameTag, championId, position)
    }
    catch (cause) {
      if (!notFound(cause)) throw cause
    }
    try {
      return await ask(nameTag, championId, null)
    }
    catch (cause) {
      if (!notFound(cause)) throw cause
      return null
    }
  }

  async function load(nameTag: string, championId: number, position: string) {
    const key = keyOf(nameTag, championId, position)
    if (key in cache.value || pending.value[key]) return
    pending.value = { ...pending.value, [key]: true }
    failed.value = { ...failed.value, [key]: false }
    try {
      const answer = await resolve(nameTag, championId, position)
      cache.value = { ...cache.value, [key]: answer?.builds.length ? answer : null }
    }
    catch {
      failed.value = { ...failed.value, [key]: true }
    }
    finally {
      pending.value = { ...pending.value, [key]: false }
    }
  }

  /** The answer for a main, or `undefined` while it has not come back. */
  const answerOf = (nameTag: string, championId: number, position: string) => cache.value[keyOf(nameTag, championId, position)]
  const isPending = (nameTag: string, championId: number, position: string) => !!pending.value[keyOf(nameTag, championId, position)]
  const hasFailed = (nameTag: string, championId: number, position: string) => !!failed.value[keyOf(nameTag, championId, position)]

  return { load, answerOf, isPending, hasFailed }
}
