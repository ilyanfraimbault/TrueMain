/**
 * Adds the browser `Cache-Control` of `server/utils/static-cache-headers.ts` to
 * successful `/api/static/*` answers (#1584). A hook rather than a route rule:
 * route-rule headers are set before the handler runs, so a failed lookup would
 * carry them too.
 */
export default defineNitroPlugin((nitroApp) => {
  nitroApp.hooks.hook('beforeResponse', (event) => {
    const cacheControl = staticDataCacheControl(event.path, getResponseStatus(event))
    if (cacheControl) setResponseHeader(event, 'cache-control', cacheControl)
  })
})
