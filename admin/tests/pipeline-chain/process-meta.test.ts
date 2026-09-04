import { describe, expect, it } from 'vitest'
import { PIPELINE_CHAIN, PROCESS_META } from '~~/shared/types/pipeline-chain'

// PIPELINE_CHAIN is a hand-maintained copy of the ingestor's JobModeSequence, and
// PROCESS_META names what is in it. Both drift silently — a missing chain entry is
// appended past the last step instead of failing (#1314), and a missing meta entry
// renders a raw C# class name instead of failing (#1316). Neither shows up in a
// build, so the coupling is pinned here instead.

describe('PIPELINE_CHAIN and PROCESS_META', () => {
  it('names every process in the chain', () => {
    const unnamed = PIPELINE_CHAIN.filter(processName => !PROCESS_META[processName])

    expect(unnamed).toEqual([])
  })

  it('describes every process in the chain', () => {
    const undescribed = PIPELINE_CHAIN.filter(
      processName => !PROCESS_META[processName]?.description?.trim(),
    )

    expect(undescribed).toEqual([])
  })

  it('has no metadata for a process that is not in the chain', () => {
    // The other direction: an entry left behind after a process is removed from the
    // pipeline is dead weight that reads as though the step still runs.
    const orphaned = Object.keys(PROCESS_META).filter(
      processName => !PIPELINE_CHAIN.includes(processName),
    )

    expect(orphaned).toEqual([])
  })

  it('lists each process exactly once', () => {
    expect(new Set(PIPELINE_CHAIN).size).toBe(PIPELINE_CHAIN.length)
  })

  it('starts at LadderSync and ends at StorageSnapshot', () => {
    // The two ends carry a stated ordering constraint: LadderSync writes the rank
    // snapshots later steps read, and StorageSnapshot must measure after retention
    // deletes rather than before.
    expect(PIPELINE_CHAIN[0]).toBe('LadderSync')
    expect(PIPELINE_CHAIN.at(-1)).toBe('StorageSnapshot')
  })
})
