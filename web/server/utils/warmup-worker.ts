import { getIpxDiskCache } from './ipx-disk-store'
import { collectIconUrls, imagePaths, patchedStaticReads, WARM_PAGES, WARM_RENDERS, WARM_STATIC_READS } from './warmup-catalog'
import { claimImageWarmup, markImagesWarm, markWorkerWarm } from './warmup-state'

const API_POLL_MS = 2000
const IMAGE_CONCURRENCY = 6
const SWEEP_INTERVAL_MS = 60 * 60 * 1000

export type LocalFetch = (path: string) => Promise<Response>

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

  const slugs = Object.values(reads.get('/api/static/champion-slugs') ?? {}) as string[]
  const championPages = slugs.map(slug => `/champions/${slug}`)

  // A renders' worth of each page, then the rest once: what is left is a champion's own data.
  const renders = [...WARM_PAGES, ...championPages.slice(0, 5)]
  for (let round = 0; round < WARM_RENDERS; round++) {
    await settleAll(renders.map(path => () => localFetch(path).then(response => response.text())), 1)
  }
  await settleAll(championPages.map(path => () => localFetch(path).then(response => response.text())), 1)

  markWorkerWarm()

  if (claimImageWarmup()) {
    const paths = imagePaths(
      [...reads.values()].flatMap(body => [...collectIconUrls(body)]),
      collectIconUrls(reads.get('/api/static/champions')),
    )
    const failures = await settleAll(paths.map(path => () => localFetch(path).then(response => response.arrayBuffer())), IMAGE_CONCURRENCY)
    markImagesWarm()
    console.info(`[warmup] ${paths.length} images checked, ${failures} failed`)

    const disk = getIpxDiskCache()
    if (disk) {
      await disk.sweep()
      setInterval(() => void disk.sweep(), SWEEP_INTERVAL_MS).unref()
    }
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
