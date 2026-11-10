/**
 * One way to ask TrueMain for something, whichever host the app runs in.
 *
 * Inside Tauri a read goes through Rust — `api.rs` says why: the webview would
 * meet CORS against an origin the site never expected, and the CSP would have
 * to open to a remote host. In `npm run dev` there is no Rust, and the dev
 * server proxies `/api` to the site's public entry point instead (nuxt.config).
 */
import { answerStaticEndpoint } from '~/utils/static-endpoints'
import { apiAnswer, shell } from '~/utils/shell'
import { asFetchError, isSuperseded } from '~/utils/shell-error'

export { isSuperseded }

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

/** How a read is sent: a signal, and whether the window's loading bar ignores it. */
export interface ReadOptions {
  /** Reaches the request in `npm run dev` only: the shell's commands cannot be cancelled. */
  signal?: AbortSignal
  /**
   * A read no click asked for — the in-game next item, re-asked as the game
   * moves. Every other read runs the loading bar while it is out (#1788).
   */
  background?: boolean
  /**
   * A key a newer request under the same key replaces this one by, inside the
   * app: the shell aborts this one and it rejects as superseded
   * (`isSuperseded`) — champion select moves faster than the API answers.
   */
  replaces?: string
}

/** A read counted by the loading bar, unless it runs in the background. */
function tracked<T>(work: Promise<T>, options: ReadOptions): Promise<T> {
  return options.background ? work : trackLoad(work)
}

/**
 * A read-only GET. Inside Tauri only the routes the shell lists are reachable
 * (`src-tauri/api-routes.json`) — it is not a general proxy onto the API.
 */
export function apiGet<T>(path: string, query: Query = {}, options: ReadOptions = {}): Promise<T> {
  return tracked((async () => {
    return await apiAnswer<T>(shell.apiGet(path, pairs(query)).catch((error: unknown) => {
      throw asFetchError(error)
    }))
  })(), options)
}

/**
 * A POST read — a read that takes a draft or a game as its body. Inside Tauri
 * only the routes the shell lists are reachable.
 */
export function apiPost<T>(path: string, body: unknown, query: Query = {}, options: ReadOptions = {}): Promise<T> {
  return tracked((async () => {
    return await apiAnswer<T>(shell.apiPost(path, pairs(query), body, options.replaces ?? null).catch((error: unknown) => {
      throw asFetchError(error)
    }))
  })(), options)
}

/**
 * Open a page of the site in the player's browser. The app shows what a
 * decision in the next thirty seconds needs; the site is where the rest lives.
 * Which site is the shell's to say (`site.rs`): a preprod build opens preprod.
 */
export async function openOnSite(path: string) {
  await shell.openOnSite(path)
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
 * player does not exist.
 */
function isResponseError(error: unknown): boolean {
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
