// @vitest-environment node
import { Buffer } from 'node:buffer'
import { mkdtemp, readdir, rm, utimes, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { createDiskCache } from '~~/server/utils/disk-cache'

const DAY_MS = 24 * 60 * 60 * 1000

interface Header {
  contentType?: string
  etag?: string
}

describe('disk cache', () => {
  let dir: string

  beforeEach(async () => {
    dir = await mkdtemp(join(tmpdir(), 'disk-cache-'))
  })

  afterEach(async () => {
    await rm(dir, { recursive: true, force: true })
  })

  const subject = (overrides = {}) => createDiskCache<Header>({ dir, maxBytes: 1024, maxAgeMs: DAY_MS, ...overrides })

  it('gives back the bytes and headers it was given', async () => {
    const cache = subject()
    const body = Buffer.from([0, 10, 255, 10, 1])

    await cache.set('/_ipx/a', { body, contentType: 'image/webp', etag: '"x"' })

    const stored = await cache.get('/_ipx/a')
    expect(stored?.contentType).toBe('image/webp')
    expect(stored?.etag).toBe('"x"')
    expect(stored?.body.equals(body)).toBe(true)
  })

  it('answers a miss for what it does not hold', async () => {
    expect(await subject().get('/_ipx/nothing')).toBeNull()
  })

  it('answers a miss rather than throwing on a file it cannot read', async () => {
    const cache = subject()
    await cache.set('/_ipx/a', { body: Buffer.from('x') })
    const [file] = await readdir(dir)
    await writeFile(join(dir, file!), 'not a header')

    expect(await cache.get('/_ipx/a')).toBeNull()
  })

  it('swallows a directory it cannot write to: an image must not fail over its cache', async () => {
    const blocked = join(dir, 'file')
    await writeFile(blocked, '')
    const cache = createDiskCache<Header>({ dir: join(blocked, 'nested'), maxBytes: 1024, maxAgeMs: DAY_MS })

    await expect(cache.set('/_ipx/a', { body: Buffer.from('x') })).resolves.toBeUndefined()
    expect(await cache.get('/_ipx/a')).toBeNull()
  })

  it('leaves no temporary file behind', async () => {
    const cache = subject()
    await cache.set('/_ipx/a', { body: Buffer.from('x') })

    expect((await readdir(dir)).filter(name => name.endsWith('.tmp'))).toEqual([])
  })

  it('sweeps the oldest files past the byte budget', async () => {
    const cache = subject({ maxBytes: 600 })
    const old = new Date(Date.now() - 2 * 60 * 1000)
    await cache.set('/_ipx/old', { body: Buffer.alloc(500) })
    const [oldFile] = await readdir(dir)
    await utimes(join(dir, oldFile!), old, old)
    await cache.set('/_ipx/new', { body: Buffer.alloc(500) })

    expect(await cache.sweep()).toBe(1)

    expect(await cache.get('/_ipx/old')).toBeNull()
    expect(await cache.get('/_ipx/new')).not.toBeNull()
  })

  it('sweeps a file past the age limit, whatever the budget says', async () => {
    const cache = subject({ now: () => Date.now() + 2 * DAY_MS })
    await cache.set('/_ipx/a', { body: Buffer.from('x') })

    expect(await cache.sweep()).toBe(1)
    expect(await cache.get('/_ipx/a')).toBeNull()
  })

  it('sweeps nothing when the directory does not exist yet', async () => {
    const cache = createDiskCache<Header>({ dir: join(dir, 'missing'), maxBytes: 1, maxAgeMs: 1 })

    expect(await cache.sweep()).toBe(0)
  })
})
