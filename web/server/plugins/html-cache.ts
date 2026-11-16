import { Buffer } from 'node:buffer'
import { getHtmlStore, htmlCacheKey, siteHost, storableHeaders } from '../utils/html-cache'

/** Stores what a render produced, if the page goes through the cache: see `utils/html-cache.ts`. */
export default defineNitroPlugin((nitroApp) => {
  if (!useRuntimeConfig().htmlCacheEnabled) return

  nitroApp.hooks.hook('render:response', (response, { event }) => {
    if (event.method !== 'GET' || response.statusCode !== 200 || event.context.renderFailed) return

    // A page prints the host it was asked on: a request forging one must not reach everyone else.
    const host = siteHost()
    if (host && getRequestHost(event) !== host) return

    const key = htmlCacheKey(event.path)
    const headers = storableHeaders({ ...getResponseHeaders(event), ...response.headers })
    if (key && headers && typeof response.body === 'string') {
      void getHtmlStore().set(key, { storedAt: Date.now(), headers, body: Buffer.from(response.body) })
    }
  })
})
