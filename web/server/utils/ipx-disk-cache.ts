import { Buffer } from 'node:buffer'
import { createHash } from 'node:crypto'
import { mkdir, readdir, readFile, rename, rm, stat, writeFile } from 'node:fs/promises'
import { join } from 'node:path'

/** What a processed image needs to be served again without IPX. */
export interface StoredImage {
  body: Buffer
  contentType?: string
  etag?: string
  lastModified?: string
}

export interface IpxDiskCacheOptions {
  dir: string
  /** What the directory may hold. {@link IpxDiskCache.sweep} removes the oldest files past it. */
  maxBytes: number
  /** A file older than this goes at the next sweep, whatever the budget says. */
  maxAgeMs: number
  now?: () => number
}

export interface IpxDiskCache {
  get: (key: string) => Promise<StoredImage | null>
  set: (key: string, image: StoredImage) => Promise<void>
  /** Enforces the age and the byte budget. Returns how many files went. */
  sweep: () => Promise<number>
}

/**
 * The processed images shared by the workers and kept across deploys, under the memory
 * cache of `server/handlers/ipx-cached.ts` (#2425). A deploy used to start every worker
 * empty, so each one fetched each icon again from Riot's CDN and the volunteer-run
 * CommunityDragon mirror, twice over (see that handler), for whoever came first.
 *
 * A failure here only costs a cache miss, so nothing throws: a full disk must not break
 * an image. The size of the directory is bounded by {@link IpxDiskCache.sweep} and by what
 * the handler agrees to store (`isDurableImagePath`), since the route is public.
 *
 * One file per image: a JSON header line, then the bytes. Written to a temporary name and
 * renamed, because another worker may read the same path at any moment.
 */
export function createIpxDiskCache(options: IpxDiskCacheOptions): IpxDiskCache {
  const { dir, maxBytes, maxAgeMs } = options
  const now = options.now ?? Date.now
  const pathOf = (key: string) => join(dir, createHash('sha256').update(key).digest('hex'))
  let temporaryFiles = 0

  return {
    async get(key) {
      try {
        const file = await readFile(pathOf(key))
        const headerEnd = file.indexOf(0x0A)
        if (headerEnd < 0) return null
        const header = JSON.parse(file.subarray(0, headerEnd).toString('utf8')) as Omit<StoredImage, 'body'>
        return { ...header, body: file.subarray(headerEnd + 1) }
      }
      catch {
        return null
      }
    },

    async set(key, { body, ...header }) {
      const target = pathOf(key)
      const temporary = `${target}.${process.pid}.${temporaryFiles++}.tmp`
      try {
        await mkdir(dir, { recursive: true })
        await writeFile(temporary, Buffer.concat([Buffer.from(`${JSON.stringify(header)}\n`), body]))
        await rename(temporary, target)
      }
      catch {
        await rm(temporary, { force: true }).catch(() => {})
      }
    },

    async sweep() {
      const names = await readdir(dir).catch(() => [] as string[])
      const files = await Promise.all(names.map(async (name) => {
        const path = join(dir, name)
        const info = await stat(path).catch(() => null)
        return info ? { path, bytes: info.size, modified: info.mtimeMs } : null
      }))

      let total = 0
      let removed = 0
      // Newest first, so what the budget cuts is what has gone longest without being written.
      for (const file of files.filter(entry => entry !== null).sort((a, b) => b.modified - a.modified)) {
        total += file.bytes
        if (total > maxBytes || now() - file.modified > maxAgeMs) {
          await rm(file.path, { force: true }).catch(() => {})
          total -= file.bytes
          removed++
        }
      }
      return removed
    },
  }
}
