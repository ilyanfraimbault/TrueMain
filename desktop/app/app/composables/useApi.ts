/**
 * One way to ask TrueMain for something, whichever host the app runs in.
 *
 * Inside Tauri a read goes through Rust — `api.rs` says why: the webview would
 * meet CORS against an origin the site never expected, and the CSP would have
 * to open to a remote host. In `npm run dev` there is no Rust, and the dev
 * server proxies `/api` to the site's public entry point instead (nuxt.config).
 */
import { answerStaticEndpoint } from '~/utils/static-endpoints'

type Query = Record<string, string | number | boolean | null | undefined>

/** The query as `[key, value]` pairs, empty values left out rather than sent blank. */
function pairs(query: Query): [string, string][] {
  return Object.entries(query)
    .filter(([, value]) => value !== null && value !== undefined && value !== '')
    .map(([key, value]) => [key, String(value)])
}

/**
 * A read-only GET. Inside Tauri only the paths `api_get` lists are reachable —
 * the shell is not a general proxy onto the API.
 */
export async function apiGet<T>(path: string, query: Query = {}): Promise<T> {
  if (insideTauri()) {
    const { invoke } = await import('@tauri-apps/api/core')
    return await invoke<T>('api_get', { path, query: pairs(query) })
  }
  return await $fetch<T>(`/api${path}`, { query: Object.fromEntries(pairs(query)) })
}

/**
 * Open a page of the site in the player's browser. The app shows what a
 * decision in the next thirty seconds needs; the site is where the rest lives.
 */
export async function openOnSite(path: string) {
  const url = `https://truemain.lol${path}`
  if (insideTauri()) {
    const { open } = await import('@tauri-apps/plugin-shell')
    await open(url)
    return
  }
  window.open(url, '_blank', 'noopener')
}

/** The options the shared pages pass to their fetcher — the site's `ApiFetchOptions`, as far as the app reads them. */
export interface ApiFetchOptions {
  query?: Query
  signal?: AbortSignal
}

/**
 * The app's answer to the site's `useApiFetch` (`web/app/composables/useApi.ts`),
 * which the shared pages of `web/layers/common` call: a path under `/api` to its
 * parsed body. TrueMain's own reads go through `apiGet`; the site's
 * `/static/*` lookups are Nitro routes the app does not have, so they are
 * answered here from Data Dragon in the same shapes (`utils/static-endpoints.ts`).
 */
export function useApiFetch() {
  return async <T>(path: string, options: ApiFetchOptions = {}): Promise<T> => {
    const local = await answerStaticEndpoint(path, options.query ?? {})
    if (local !== undefined) return local as T
    return await apiGet<T>(path, options.query)
  }
}
