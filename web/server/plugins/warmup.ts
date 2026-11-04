import cluster from 'node:cluster'
import { renderRequestHeaders } from '../utils/html-cache'
import { markWorkerWarm } from '../utils/warmup-state'
import { type LocalFetch, warmWorker } from '../utils/warmup-worker'

/**
 * Warms this worker before the site opens (#2425). `/api/readyz` stays down until every worker
 * has finished (`warmup-state.ts`), which keeps the visitor on the maintenance page.
 *
 * Each worker renders for itself: V8's optimised code and the Nitro cached functions are per
 * process. The processed images and the rendered champion pages are on a disk the workers share, so
 * only one of them does those. It fetches only the images the previous deploys have not kept: a
 * redeploy costs the upstream CDNs next to nothing, which is what made a boot pre-warm unacceptable
 * before (#997).
 *
 * Off unless `NUXT_WARMUP_ENABLED` is set. A failed request is left for the visitor who asks,
 * and a stuck warm-up cannot keep the site down past the budget in `readyz.get.ts`.
 */
export default defineNitroPlugin((nitroApp) => {
  const { warmupEnabled, apiBaseUrl } = useRuntimeConfig()

  // The cluster's primary loads the same bundle, plugins included, and serves nothing.
  if (!warmupEnabled || cluster.isPrimary) return

  // Always a render, past the HTML cache: what is warmed here is this worker.
  const localFetch: LocalFetch = async (path, headers) => {
    const response = await nitroApp.localFetch(path, { headers: { ...headers, ...renderRequestHeaders() } })
    if (response.status >= 500) throw new Error(`${path} answered ${response.status}`)
    return response
  }

  warmWorker(localFetch, apiBaseUrl).catch((error) => {
    console.error('[warmup] this worker gave up warming', error)
    markWorkerWarm()
  })
})
