import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, ref, toValue } from 'vue'
import { fillPath } from '#site-shared/contract/routes'
import * as accounts from '~/composables/useOpsAccounts'
import * as audit from '~/composables/useOpsAudit'
import * as dataQuality from '~/composables/useOpsDataQuality'
import * as personas from '~/composables/useOpsPersonas'
import * as pipeline from '~/composables/useOpsPipeline'
import * as stats from '~/composables/useOps'
import { useDesktopUsage } from '~/composables/useDesktopUsage'
import { useHealthAlerts } from '~/composables/useHealthAlerts'
import { useIngestorMetrics } from '~/composables/useIngestorMetrics'
import { useRequestLatency } from '~/composables/useRequestLatency'
import { useRiotQuota } from '~/composables/useRiotQuota'
import { cleanQuery, opsCacheKey } from '~/utils/ops-cache'
import { opsCacheFetchOptions, trackOpsCacheShown } from '~/utils/ops-cache-hooks'

// The admin's one ops client (#2510): every read and write reaches the backend route the
// contract names, with its `{name}` segments filled and encoded, its query and its body.

const fetcher = vi.fn()
const useFetch = vi.fn()

beforeEach(() => {
  fetcher.mockReset().mockResolvedValue({})
  useFetch.mockReset().mockImplementation(() => ({ status: ref('success'), data: ref(null) }))
  vi.stubGlobal('$fetch', fetcher)
  vi.stubGlobal('useFetch', useFetch)
  vi.stubGlobal('useUserSession', () => ({ loggedIn: ref(true) }))
  vi.stubGlobal('computed', computed)
  vi.stubGlobal('toValue', toValue)
  vi.stubGlobal('cleanQuery', cleanQuery)
  vi.stubGlobal('opsCacheKey', opsCacheKey)
  vi.stubGlobal('opsCacheFetchOptions', opsCacheFetchOptions)
  vi.stubGlobal('trackOpsCacheShown', trackOpsCacheShown)
  vi.stubGlobal('useOps', stats.useOps)
  vi.stubGlobal('opsCall', stats.opsCall)
})

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('fillPath', () => {
  it('encodes each segment', () => {
    expect(fillPath('/accounts/{nameTag}', { nameTag: 'Faker#KR 1' })).toBe('/accounts/Faker%23KR%201')
  })

  it('refuses a template whose segment is missing', () => {
    expect(() => fillPath('/personas/{id}/links/{linkId}', { id: 'p' })).toThrow('linkId')
  })
})

describe('one-off calls', () => {
  const cases: [string, () => Promise<unknown>, string, Record<string, unknown>][] = [
    ['candidate detail', () => accounts.getCandidateDetail('c 1'), '/api/ops/candidates/c%201', { method: 'GET' }],
    ['account explorer', () => accounts.getAccountExplorer('Faker#KR1', 'KR'), '/api/ops/accounts/Faker%23KR1', { method: 'GET', query: { region: 'KR' } }],
    ['account explorer, any region', () => accounts.getAccountExplorer('Faker#KR1'), '/api/ops/accounts/Faker%23KR1', { method: 'GET', query: undefined }],
    ['seed request', () => accounts.getSeedRequest('s1'), '/api/ops/accounts/seed/s1', { method: 'GET' }],
    ['seed', () => accounts.seedAccount({ gameName: 'A', tagLine: 'B', platformId: 'EUW1' }), '/api/ops/accounts/seed', { method: 'POST', body: { gameName: 'A', tagLine: 'B', platformId: 'EUW1' } }],
    ['audit trail', () => audit.getAuditEvents({ targetId: 'p1' }), '/api/ops/audit-events', { method: 'GET', query: { targetId: 'p1' } }],
    ['aggregate freshness', () => dataQuality.getAggregateFreshness(), '/api/ops/data-quality/aggregate-freshness', { method: 'GET' }],
    ['match data quality', () => dataQuality.getMatchDataQuality('EUW1_1'), '/api/ops/data-quality/match/EUW1_1', { method: 'GET' }],
    ['conflict', () => personas.getPersonaConflict('k1'), '/api/ops/personas/conflicts/k1', { method: 'GET' }],
    ['resolve', () => personas.resolvePersonaConflict('k1', { action: 'merge', keep: 'owner' }), '/api/ops/personas/conflicts/k1/resolve', { method: 'POST', body: { action: 'merge', keep: 'owner' } }],
    ['bulk preview', () => personas.previewBulkResolve(20), '/api/ops/personas/conflicts/bulk-resolve/preview', { method: 'GET', query: { minScore: 20 } }],
    ['bulk resolve', () => personas.bulkResolvePersonaConflicts({ ids: ['k1'], minScore: 20 }), '/api/ops/personas/conflicts/bulk-resolve', { method: 'POST', body: { ids: ['k1'], minScore: 20 } }],
    ['auto-merge', () => personas.autoMergePersonaConflicts(), '/api/ops/personas/conflicts/auto-merge', { method: 'POST' }],
    ['homonym merge', () => personas.mergePersonaHomonyms({ keepId: 'a', mergeIds: ['b'] }), '/api/ops/personas/homonyms/merge', { method: 'POST', body: { keepId: 'a', mergeIds: ['b'] } }],
    ['homonym distinct', () => personas.markPersonaHomonymsDistinct({ personaIds: ['a', 'b'] }), '/api/ops/personas/homonyms/distinct', { method: 'POST', body: { personaIds: ['a', 'b'] } }],
    ['palette search', () => personas.searchPersonas('caps', 5), '/api/ops/personas', { method: 'GET', query: { search: 'caps', pageSize: 5 } }],
    ['persona', () => personas.getPersona('p1'), '/api/ops/personas/p1', { method: 'GET' }],
    ['create', () => personas.createPersona({ slug: 's', displayName: 'S' }), '/api/ops/personas', { method: 'POST', body: { slug: 's', displayName: 'S' } }],
    ['rename', () => personas.renamePersona('p1', 'New'), '/api/ops/personas/p1', { method: 'PATCH', body: { displayName: 'New' } }],
    ['remove', () => personas.removePersona('p1', false), '/api/ops/personas/p1', { method: 'DELETE', query: undefined }],
    ['take down', () => personas.removePersona('p1', true), '/api/ops/personas/p1', { method: 'DELETE', query: { takedown: true } }],
    ['add link', () => personas.addPersonaLink('p1', { kind: 'twitch', url: 'https://twitch.tv/x' }), '/api/ops/personas/p1/links', { method: 'POST', body: { kind: 'twitch', url: 'https://twitch.tv/x' } }],
    ['remove link', () => personas.removePersonaLink('p1', 'l/1'), '/api/ops/personas/p1/links/l%2F1', { method: 'DELETE' }],
    ['add account', () => personas.addPersonaAccount('p1', { gameName: 'A', tagLine: 'B', platformId: 'KR' }), '/api/ops/personas/p1/accounts', { method: 'POST', body: { gameName: 'A', tagLine: 'B', platformId: 'KR' } }],
    ['unlink account', () => personas.unlinkPersonaAccount('p1', 'a1'), '/api/ops/personas/p1/accounts/a1', { method: 'DELETE' }],
  ]

  it.each(cases)('%s', async (_, call, url, options) => {
    await call()
    expect(fetcher).toHaveBeenCalledWith(url, expect.objectContaining(options))
  })
})

describe('watched reads', () => {
  const cases: [string, () => unknown, string][] = [
    ['overview', () => stats.useOverviewStats(), '/api/ops/stats/overview'],
    ['champion stats', () => stats.useChampionStats({ region: 'KR' }), '/api/ops/stats/champions'],
    ['matches over time', () => stats.useMatchesOverTime('day'), '/api/ops/stats/matches-over-time'],
    ['matches ingested', () => stats.useMatchesIngested('day', 7), '/api/ops/stats/matches-ingested'],
    ['tables', () => stats.useDbTables(), '/api/ops/db/tables'],
    ['storage history', () => stats.useDbStorageHistory(30), '/api/ops/db/history'],
    ['aggregations', () => stats.useAggregations(), '/api/ops/stats/aggregations'],
    ['patch coverage', () => stats.usePatchCoverage(), '/api/ops/patch-coverage'],
    ['process runs', () => pipeline.useProcessRuns({ page: 2 }), '/api/ops/process-runs'],
    ['process iterations', () => pipeline.useProcessIterations({ page: 1 }), '/api/ops/process-iterations'],
    ['process rollup', () => pipeline.useProcessRollup({ processName: 'Discovery' }), '/api/ops/process-runs/rollup'],
    ['latest passes', () => pipeline.useLatestProcessIterations(), '/api/ops/process-iterations/latest'],
    ['logs', () => pipeline.useLogs({ level: 'Warning' }), '/api/ops/logs'],
    ['crashes', () => pipeline.useCrashes({ page: 1 }), '/api/ops/crashes'],
    ['riot usage', () => pipeline.useRiotUsage({ window: '24h' }), '/api/ops/riot-usage'],
    ['pipeline health', () => pipeline.usePipelineHealth(), '/api/ops/pipeline-health'],
    ['configuration', () => pipeline.useEffectiveConfiguration(), '/api/ops/configuration'],
    ['audit', () => audit.useAuditEvents({ action: 'x' }), '/api/ops/audit-events'],
    ['seed queue', () => accounts.useSeedRequests({ page: 1 }), '/api/ops/accounts/seed'],
    ['candidates', () => accounts.useCandidates({ page: 1 }), '/api/ops/candidates'],
    ['candidate funnel', () => accounts.useCandidateFunnel('day', 30), '/api/ops/candidates/funnel'],
    ['candidate stock', () => accounts.useCandidateStock('hour', 7), '/api/ops/candidates/stock'],
    ['queue latency', () => accounts.useCandidateQueueLatency(), '/api/ops/candidates/queue-latency'],
    ['incomplete matches', () => dataQuality.useIncompleteMatches({ page: 1 }), '/api/ops/data-quality/incomplete-matches'],
    ['detectors', () => dataQuality.useDataQualityDetectors(), '/api/ops/data-quality/detectors'],
    ['incomplete match counts', () => dataQuality.useIncompleteMatchCounts({ queue: 420 }), '/api/ops/data-quality/incomplete-matches/counts'],
    ['conflicts', () => personas.usePersonaConflicts({ status: 'Open' }), '/api/ops/personas/conflicts'],
    ['channel conflicts', () => personas.usePersonaChannelConflicts({ page: 1 }), '/api/ops/personas/channel-conflicts'],
    ['homonyms', () => personas.usePersonaHomonyms({ page: 1 }), '/api/ops/personas/homonyms'],
    ['known players', () => personas.usePersonas({ page: 1 }), '/api/ops/personas'],
    ['riot quota', () => useRiotQuota('24h'), '/api/ops/riot-quota'],
    ['request latency', () => useRequestLatency('7d'), '/api/ops/request-latency'],
    ['ingestor metrics', () => useIngestorMetrics('24h'), '/api/ops/ingestor-metrics'],
    ['health alerts', () => useHealthAlerts(), '/api/ops/health-alerts'],
    ['desktop usage', () => useDesktopUsage(30), '/api/ops/desktop/usage'],
  ]

  it.each(cases)('%s', (_, read, url) => {
    read()
    expect(useFetch).toHaveBeenCalledWith(url, expect.anything())
  })

  it('follows a reactive filter and drops the empty ones', () => {
    const page = ref(1)
    pipeline.useProcessRuns(() => ({ page: page.value, processName: undefined }))
    const query = useFetch.mock.calls[0]![1].query
    expect(query.value).toEqual({ page: 1 })
    page.value = 3
    expect(query.value).toEqual({ page: 3 })
  })

  it('reads as pending only while nothing is on screen', () => {
    const status = ref('pending')
    const data = ref<unknown>(null)
    useFetch.mockImplementation(() => ({ status, data }))
    const fetched = stats.useOverviewStats()
    expect(fetched.pending.value).toBe(true)
    data.value = { total: 1 }
    expect(fetched.pending.value).toBe(false)
    expect(fetched.refreshing.value).toBe(true)
  })
})
