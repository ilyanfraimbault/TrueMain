import { getHtmlStore } from './html-cache'
import { getIpxDiskCache } from './ipx-disk-store'
import {
  championReads,
  collectIconUrls,
  imagePaths,
  patchedStaticReads,
  WARM_PAGES,
  WARM_RENDERED_CHAMPIONS,
  WARM_RENDERS,
  WARM_STATIC_READS,
} from './warmup-catalog'
import { claimSharedWarmup, markSharedWarm, markWorkerWarm } from './warmup-state'

const API_POLL_MS = 2000
const IMAGE_CONCURRENCY = 6
const PAGE_CONCURRENCY = 2
const SWEEP_INTERVAL_MS = 60 * 60 * 1000

export type LocalFetch = (path: string, headers?: Record<string, string>) => Promise<Response>

/**
 * A documentation address per champion page (RFC 5737), never routed. Rendering them all is a few
 * thousand API reads in under a minute, from what the API would otherwise see as one client; the
 * rate limit has no reason to apply and a throttled render would not be stored.
 */
function warmClient(index: number): Record<string, string> {
  return { 'x-forwarded-for': `203.0.113.${(index % 254) + 1}` }
}

/** Warms this worker, and the shared images if it is the one that gets to. */
export async function warmWorker(
  localFetch: LocalFetch,
  apiBaseUrl: string,
  { apiPollMs = API_POLL_MS }: { apiPollMs?: number } = {},
): Promise<void> {
  const startedAt = Date.now()
  await waitForApi(apiBaseUrl, apiPollMs)

  const reads = new Map<string, unknown>()
  const read = async (path: string) => {
    const body = await (await localFetch(path)).json().catch(() => null)
    reads.set(path, body)
    return body
  }

  await settleAll(WARM_STATIC_READS.map(path => () => read(path)), 1)
  const versions = reads.get('/api/static/versions')
  const version = Array.isArray(versions) && typeof versions[0] === 'string' ? versions[0] : null
  const patched = version ? patchedStaticReads(version) : []
  await settleAll(patched.map(path => () => read(path)), 1)

  const slugs = (reads.get('/api/static/champion-slugs') ?? {}) as Record<string, string>
  const renders = [
    ...WARM_PAGES,
    ...Object.values(slugs).slice(0, WARM_RENDERED_CHAMPIONS).map(slug => `/champions/${slug}`),
  ]
  for (let round = 0; round < WARM_RENDERS; round++) {
    await settleAll(renders.map(path => () => localFetch(path).then(response => response.text())), 1)
  }

  const championPaths = version ? Object.keys(slugs).flatMap(id => championReads(id, version)) : []
  await settleAll(championPaths.map(path => () => localFetch(path).then(response => response.text())), 1)

  markWorkerWarm()

  if (claimSharedWarmup()) {
    const paths = imagePaths(
      [...reads.values()].flatMap(body => [...collectIconUrls(body)]),
      collectIconUrls(reads.get('/api/static/champions')),
    )
    const failures = await settleAll(paths.map(path => () => localFetch(path).then(response => response.arrayBuffer())), IMAGE_CONCURRENCY)
    console.info(`[warmup] ${paths.length} images checked, ${failures} failed`)

    // Stored as they render, for every worker to answer from (`html-cache.ts`).
    const pages = Object.values(slugs).map(slug => `/champions/${slug}`)
    const pageFailures = await settleAll(
      pages.map((path, index) => () => localFetch(path, warmClient(index)).then(response => response.text())),
      PAGE_CONCURRENCY,
    )
    console.info(`[warmup] ${pages.length} champion pages rendered, ${pageFailures} failed`)
    markSharedWarm()

    const disk = getIpxDiskCache()
    if (disk) {
      await disk.sweep()
      setInterval(() => void disk.sweep(), SWEEP_INTERVAL_MS).unref()
    }

    const htmlStore = getHtmlStore()
    setInterval(() => void htmlStore.sweep(), SWEEP_INTERVAL_MS).unref()
  }

  console.info(`[warmup] worker ${process.pid} warm in ${Math.round((Date.now() - startedAt) / 1000)}s`)
}

/** Web has nothing to warm against until the API has finished warming itself. */
async function waitForApi(apiBaseUrl: string, pollMs: number): Promise<void> {
  while (true) {
    const status = await $fetch.raw(`${apiBaseUrl}/readyz`, { retry: false, ignoreResponseError: true })
      .then(response => response.status, () => null)
    if (status === 200) return
    await new Promise(resolve => setTimeout(resolve, pollMs))
  }
}

/** Runs every task, `concurrency` at a time, and answers how many failed. */
async function settleAll(tasks: Array<() => Promise<unknown>>, concurrency: number): Promise<number> {
  let next = 0
  let failures = 0
  await Promise.all(Array.from({ length: concurrency }, async () => {
    while (next < tasks.length) {
      const task = tasks[next++]!
      await task().catch(() => {
        failures++
      })
    }
  }))
  return failures
}
