/**
 * One way to ask TrueMain for something, whichever host the app runs in.
 *
 * Inside Tauri a read goes through Rust — `api.rs` says why: the webview would
 * meet CORS against an origin the site never expected, and the CSP would have
 * to open to a remote host. In `npm run dev` there is no Rust, and the dev
 * server proxies `/api` to the site's public entry point instead (nuxt.config).
 */
import { answerStaticEndpoint } from '~/utils/static-endpoints'

type QueryValue = string | number | boolean | null | undefined
/** A list repeats its key, one pair per entry (`?player=a&player=b`). */
type Query = Record<string, QueryValue | readonly QueryValue[]>

/** The query as `[key, value]` pairs, empty values left out rather than sent blank. */
function pairs(query: Query): [string, string][] {
  return Object.entries(query)
    .flatMap(([key, value]) => (Array.isArray(value) ? value : [value]).map(entry => [key, entry] as const))
    .filter(([, value]) => value !== null && value !== undefined && value !== '')
    .map(([key, value]) => [key, String(value)])
}

/** The pairs as `$fetch` takes them: a key met twice becomes a list. */
function fetchQuery(query: Query): Record<string, string | string[]> {
  const result: Record<string, string | string[]> = {}
  for (const [key, value] of pairs(query)) {
    const known = result[key]
    result[key] = known === undefined ? value : [...(Array.isArray(known) ? known : [known]), value]
  }
  return result
}

/** How a read is sent: a signal, and whether the window's loading bar ignores it. */
export interface ReadOptions {
  /** Reaches the request in `npm run dev` only: the shell's commands cannot be cancelled. */
  signal?: AbortSignal
  /**
   * A read no click asked for — the in-game next item, re-asked as the game
   * moves. Every other read runs the loading bar while it is out (#1788).
   */
  background?: boolean
}

/** A read counted by the loading bar, unless it runs in the background. */
function tracked<T>(work: Promise<T>, options: ReadOptions): Promise<T> {
  return options.background ? work : trackLoad(work)
}

/**
 * A read-only GET. Inside Tauri only the paths `api_get` lists are reachable —
 * the shell is not a general proxy onto the API.
 */
export function apiGet<T>(path: string, query: Query = {}, options: ReadOptions = {}): Promise<T> {
  return tracked((async () => {
    if (insideTauri()) {
      const { invoke } = await import('@tauri-apps/api/core')
      return await invoke<T>('api_get', { path, query: pairs(query) })
    }
    return await $fetch<T>(`/api${path}`, { query: fetchQuery(query), signal: options.signal })
  })(), options)
}

/**
 * A POST read — the composition build and its games, which take the draft as
 * a body. Inside Tauri only the paths `api_post` lists are reachable.
 */
export function apiPost<T>(path: string, body: unknown, query: Query = {}, options: ReadOptions = {}): Promise<T> {
  return tracked((async () => {
    if (insideTauri()) {
      const { invoke } = await import('@tauri-apps/api/core')
      return await invoke<T>('api_post', { path, query: pairs(query), request: body })
    }
    return await $fetch<T>(`/api${path}`, { method: 'POST', body: body as Record<string, unknown>, query: fetchQuery(query), signal: options.signal })
  })(), options)
}

/**
 * Open a page of the site in the player's browser. The app shows what a
 * decision in the next thirty seconds needs; the site is where the rest lives.
 * Which site is the shell's to say (`site.rs`): a preprod build opens preprod.
 */
export async function openOnSite(path: string) {
  if (insideTauri()) {
    const { invoke } = await import('@tauri-apps/api/core')
    await invoke('open_on_site', { path })
    return
  }
  window.open(`${import.meta.env.TRUEMAIN_SITE_URL}${path}`, '_blank', 'noopener')
}

/** The options the shared pages pass to their fetcher — the site's `ApiFetchOptions`, as far as the app reads them. */
export interface ApiFetchOptions {
  query?: Query
  method?: 'GET' | 'POST'
  body?: unknown
  signal?: AbortSignal
  /** As on the site: an HTTP error status resolves `null` instead of throwing (a 404 read as "not found"). */
  ignoreResponseError?: boolean
}

/**
 * Whether a failed read got an answer from TrueMain — an HTTP error status —
 * rather than none at all. Only the first is what `ignoreResponseError` turns
 * into `null`: an offline app must say it cannot reach TrueMain, not that a
 * player does not exist. The shell reports a status as `TrueMain answered …`
 * (`api.rs`); in `npm run dev` the error is ofetch's, carrying the status.
 */
function isResponseError(error: unknown): boolean {
  if (typeof error === 'string') return error.startsWith('TrueMain answered')
  return typeof (error as { statusCode?: unknown } | null)?.statusCode === 'number'
}

/**
 * The app's answer to the site's `useApiFetch` (`web/app/composables/useApi.ts`),
 * which the shared pages of `web/layers/common` call: a path under `/api` to its
 * parsed body. TrueMain's own reads go through `apiGet` / `apiPost`; the site's
 * `/static/*` lookups are Nitro routes the app does not have, so they are
 * answered here from Data Dragon in the same shapes (`utils/static-endpoints.ts`).
 */
export function useApiFetch() {
  return async <T>(path: string, options: ApiFetchOptions = {}): Promise<T> => {
    const query = options.query ?? {}
    if (options.method !== 'POST') {
      const local = await answerStaticEndpoint(path, query)
      if (local !== undefined) return local as T
    }
    try {
      return options.method === 'POST'
        ? await apiPost<T>(path, options.body, query, { signal: options.signal })
        : await apiGet<T>(path, query, { signal: options.signal })
    }
    catch (error) {
      if (options.ignoreResponseError && isResponseError(error)) return null as T
      throw error
    }
  }
}
