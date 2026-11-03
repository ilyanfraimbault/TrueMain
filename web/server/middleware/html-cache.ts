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
  void useNitroApp().localFetch(path, { headers: renderRequestHeaders() })
    .catch(() => {})
    .finally(() => rendering.delete(path))
}
