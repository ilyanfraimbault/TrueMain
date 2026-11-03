import type { $Fetch } from 'nitropack/types'
import { describeFetchError, fetchErrorStatus } from '#common/utils/errors'

/** Where every call these helpers make lands: the Nitro proxy in `server/api/[...path].ts`. */
export const API_BASE_URL = '/api'

/** `$fetch` options minus `baseURL`, which these helpers own. */
export type ApiFetchOptions = Omit<NonNullable<Parameters<typeof $fetch>[1]>, 'baseURL'>

/** A path under `/api` (`/champions/266`, not `/api/champions/266`) → the parsed body. */
export type ApiFetch = <T>(path: string, options?: ApiFetchOptions) => Promise<T>

/**
 * An HTTP failure rethrown as an error that carries its status and the app's own copy
 * (`describeFetchError`) — never the proxied URL, never the backend's body. A raw
 * ofetch message reads `[GET] "/api/champions/1": 500 …`, and `error.vue` or a stray
 * `{{ error.message }}` would print it as is.
 *
 * Failures without a status (network drop, abort) pass through untouched: Nuxt
 * recognises a cancelled request by its `AbortError`, and replacing it would turn a
 * superseded fetch into a reported one. Their copy still comes from
 * `describeFetchError` wherever they are shown.
 */
export function normalizeApiError(error: unknown): unknown {
  const statusCode = fetchErrorStatus(error)
  if (statusCode === undefined) return error
  return createError({
    statusCode,
    statusMessage: (error as { statusMessage?: string }).statusMessage,
    message: describeFetchError(error),
  })
}

let reloadingForMaintenance = false

/**
 * Takes a tab that is already open to the maintenance page (#2412). The page is a
 * document the edge serves, so a client-side navigation never asks for it: all it sees
 * is the edge's JSON `503` on `/api/*` (`compose.*.edge.yaml`), which would otherwise
 * read as one failed panel on a site that looks alive. A reload lands on the page, and
 * the page reloads itself once the site is back.
 *
 * The body is what tells it from a `503` the application answered, which stays an
 * ordinary error. Once is enough: several calls usually fail together.
 */
function reloadOnEdgeMaintenance(error: unknown): void {
  if (!import.meta.client || reloadingForMaintenance || fetchErrorStatus(error) !== 503) return
  if ((error as { data?: { error?: unknown } }).data?.error !== 'maintenance') return
  reloadingForMaintenance = true
  location.reload()
}

/**
 * The fetcher for a hand-written `useAsyncData` handler that calls the backend.
 * Call it in setup, not inside the handler: it resolves `useRequestFetch()`, which
 * needs the Nuxt context.
 *
 * On the server `useRequestFetch()` forwards the visitor's `X-Forwarded-For`; a bare
 * `$fetch('/api/…')` there is an in-process call carrying no header at all, and the
 * API puts every SSR call of every visitor in one rate-limit bucket (#1557). In the
 * browser it is the plain `$fetch`. Going through this helper is what makes the rule
 * structural rather than a thing each composable has to remember.
 *
 * Errors come out normalised (`normalizeApiError`) after ofetch's own retry has run.
 */
export function useApiFetch(): ApiFetch {
  const requestFetch = useRequestFetch() as unknown as <T>(path: string, options?: object) => Promise<T>
  return async <T>(path: string, options?: ApiFetchOptions) => {
    try {
      return await requestFetch<T>(path, { ...options, baseURL: API_BASE_URL })
    }
    catch (error: unknown) {
      reloadOnEdgeMaintenance(error)
      throw normalizeApiError(error)
    }
  }
}

/**
 * `useFetch` for the backend: paths are relative to `/api`, the request goes through
 * `useApiFetch` (visitor forwarded during SSR, errors normalised). The default for any
 * declarative backend call — a composable whose handler needs logic (a 404 that means
 * "empty", a gate that resolves a placeholder) uses `useApiFetch` inside
 * `useAsyncData` instead.
 *
 * The options are a function so they are resolved per call, in the caller's setup
 * context, and so a caller cannot override the base or the fetcher.
 *
 * Not for the hand-rolled fetchers (`useTruemainFetch`, `useCompositionBuild`,
 * `useCompositionBuildGames`, `useTruemainSearch`): their payloads are per viewer and
 * must never enter the shared SSR payload that `useFetch` writes to — see
 * `decisions/web-frontend-rules.md`.
 */
export const useApi = createUseFetch(() => ({
  baseURL: API_BASE_URL,
  // `createUseFetch` only ever calls `$fetch` as a function; `ApiFetch` has none of
  // `$Fetch`'s `raw` / `create` members, and nothing here needs them.
  $fetch: useApiFetch() as unknown as $Fetch,
}))
