// @vitest-environment node
import { mkdtempSync, rmSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { claimSharedWarmup, clusterWorkers, isSiteWarm, markSharedWarm, markWorkerWarm } from '~~/server/utils/warmup-state'

const FAR_BUDGET = Number.MAX_SAFE_INTEGER

describe('warm-up state', () => {
  let dir: string

  beforeEach(() => {
    dir = mkdtempSync(join(tmpdir(), 'warmup-state-'))
    vi.stubEnv('TMPDIR', dir)
  })

  afterEach(() => {
    vi.unstubAllEnvs()
    rmSync(dir, { recursive: true, force: true })
  })

  it('is warm once every worker and what they share are done', () => {
    expect(isSiteWarm(1, FAR_BUDGET)).toBe(false)

    markWorkerWarm()
    expect(isSiteWarm(1, FAR_BUDGET)).toBe(false)

    markSharedWarm()
    expect(isSiteWarm(1, FAR_BUDGET)).toBe(true)
  })

  it('waits for the workers it has not heard from', () => {
    markWorkerWarm()
    markSharedWarm()

    expect(isSiteWarm(3, FAR_BUDGET)).toBe(false)
  })

  it('opens past the budget, whatever is left to warm', () => {
    expect(isSiteWarm(3, 0)).toBe(true)
  })

  it('lets one worker claim what they share', () => {
    expect(claimSharedWarmup()).toBe(true)
    expect(claimSharedWarmup()).toBe(false)
  })

  it('reads the worker count the way the cluster preset does', () => {
    vi.stubEnv('NITRO_CLUSTER_WORKERS', '3')
    expect(clusterWorkers()).toBe(3)

    vi.stubEnv('NITRO_CLUSTER_WORKERS', '')
    expect(clusterWorkers()).toBe(1)

    vi.stubEnv('NITRO_CLUSTER_WORKERS', 'many')
    expect(clusterWorkers()).toBe(1)
  })
})
