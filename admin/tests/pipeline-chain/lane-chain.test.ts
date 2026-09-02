import { describe, expect, it } from 'vitest'
import {
  AGGREGATE_LANE_CHAIN,
  FETCH_LANE_CHAIN,
  PIPELINE_CHAIN,
  resolvePipelineChain,
} from '~~/shared/types/ops'

// Since #1362 a pass runs one lane, not the whole sequence. Drawing every iteration
// against PIPELINE_CHAIN rendered the other lane's twelve steps as "not run", so a
// complete fetch-lane pass read as a half-broken pipeline. These pin the partition
// (which the backend also asserts, in IngestorProcessRegistrationTests) and the
// resolution rule.

describe('lane chains', () => {
  it('partition the full pipeline', () => {
    expect([...FETCH_LANE_CHAIN, ...AGGREGATE_LANE_CHAIN].sort()).toEqual([...PIPELINE_CHAIN].sort())
    expect(FETCH_LANE_CHAIN.filter(step => AGGREGATE_LANE_CHAIN.includes(step))).toEqual([])
  })

  it('keep the relative order of the full pipeline', () => {
    for (const lane of [FETCH_LANE_CHAIN, AGGREGATE_LANE_CHAIN]) {
      const positions = lane.map(step => PIPELINE_CHAIN.indexOf(step))
      expect(positions).toEqual([...positions].sort((a, b) => a - b))
    }
  })
})

describe('resolvePipelineChain', () => {
  it('draws a lane pass against its own lane', () => {
    expect(resolvePipelineChain('FetchLane', ['LadderSync'])).toBe(FETCH_LANE_CHAIN)
    expect(resolvePipelineChain('AggregateLane', ['MainAnalysis'])).toBe(AGGREGATE_LANE_CHAIN)
  })

  it('draws a full pass against the whole sequence', () => {
    expect(resolvePipelineChain('Full', ['LadderSync', 'MainAnalysis'])).toBe(PIPELINE_CHAIN)
  })

  it('draws a single-process mode as only what ran', () => {
    // A one-off `RetentionOnly` is not a pass through a chain; showing eleven other
    // steps would claim they were skipped.
    expect(resolvePipelineChain('MatchDataRetentionOnly', ['MatchDataRetention']))
      .toEqual(['MatchDataRetention'])
  })

  it('infers the lane from the processes when no mode was recorded', () => {
    // Runs written before the mode was stamped. The two lanes share no process, so a
    // non-empty set answers unambiguously.
    expect(resolvePipelineChain(null, ['LadderSync', 'Harvest'])).toBe(FETCH_LANE_CHAIN)
    expect(resolvePipelineChain(null, ['MainAnalysis', 'MatchDataRetention'])).toBe(AGGREGATE_LANE_CHAIN)
  })

  it('falls back to the full chain when the processes span both lanes', () => {
    expect(resolvePipelineChain(null, ['LadderSync', 'MainAnalysis'])).toBe(PIPELINE_CHAIN)
    expect(resolvePipelineChain(null, [])).toBe(PIPELINE_CHAIN)
  })
})
