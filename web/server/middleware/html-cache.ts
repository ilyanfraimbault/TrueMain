import { getHtmlStore, HTML_RENDER_HEADER, HTML_RENDER_KEY, htmlCacheKey, htmlPageState, renderRequestHeaders } from '../utils/html-cache'

const rendering = new Set<string>()

/** Answers a page from the shared cache before anything renders it: see `utils/html-cache.ts`. */
export default defineEventHandler(async (event) => {
  if (event.method !== 'GET' || !useRuntimeConfig(event).htmlCacheEnabled) return
  if (getRequestHeader(event, HTML_RENDER_HEADER) === HTML_RENDER_KEY) return

  const key = htmlCacheKey(event.path)
  const stored = key ? await getHtmlStore().get(key) : null
  if (!stored) return

  const state = htmlPageState(stored.storedAt, Date.now())
  if (state === 'expired') return
  if (state === 'stale') renderInBackground(event.path)

  setResponseHeaders(event, stored.headers)
  setResponseHeader(event, 'x-html-cache', state)
  return stored.body
})

function renderInBackground(path: string): void {
  if (rendering.has(path)) return
  rendering.add(path)
  const nitroApp = useNitroApp()
  // A render that fails is reported by the error hook like any request; this
  // catches the one that never got that far, so a stale page served for the
  // next hour is never served in silence.
  void nitroApp.localFetch(path, { headers: renderRequestHeaders() })
    .catch((error: unknown) => nitroApp.captureError(error as Error, { tags: ['html-cache'] }))
    .finally(() => rendering.delete(path))
}
