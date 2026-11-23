import { mount } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { computed } from 'vue'
import type { DbStorageHistoryReadModel } from '#site-shared/contract/api'
import DatabaseForecast from '~/components/database/DatabaseForecast.vue'

// The forecast card names why there is no forecast from the API's own reason, with the
// API's own minimum, rather than re-deriving the rule.
const global = {
  stubs: { UCard: { template: '<div><slot name="header" /><slot /></div>' }, PanelTitle: true, UBadge: true, USkeleton: true },
}

function history(overrides: Partial<DbStorageHistoryReadModel>): DbStorageHistoryReadModel {
  return {
    daily: [],
    tables: [],
    engines: [],
    comparableDays: 0,
    forecast: null,
    forecastAbsence: null,
    forecastMinimumDays: 3,
    ...overrides,
  } as DbStorageHistoryReadModel
}

function reasonFor(overrides: Partial<DbStorageHistoryReadModel>) {
  return mount(DatabaseForecast, { props: { history: history(overrides), pending: false }, global }).text()
}

beforeEach(() => {
  vi.stubGlobal('computed', computed)
  vi.stubGlobal('storageEngineLabel', (engine: string) => engine)
})

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('DatabaseForecast', () => {
  it('names the reason the API gives for no forecast', () => {
    expect(reasonFor({ forecastAbsence: 'NoSnapshots' })).toContain('No snapshots recorded yet')
    expect(reasonFor({ forecastAbsence: 'NoCapacityConfigured' })).toContain('No disk capacity is configured')
    expect(reasonFor({ forecastAbsence: 'NotGrowing' })).toContain('flat or shrinking')
  })

  it('counts the days a trend still needs from the API\'s minimum', () => {
    const day = { dateUtc: '2026-11-01T00:00:00Z', databaseBytes: 1, postgresBytes: 1, mongoBytes: 0, tableCount: 1 }
    expect(reasonFor({ forecastAbsence: 'TooFewDays', daily: [day, day] as never, comparableDays: 2 }))
      .toContain('Only 2 day(s) of history — 3 are needed')
    expect(reasonFor({ forecastAbsence: 'EnginesChanged', daily: [day, day] as never, comparableDays: 1 }))
      .toContain('changed 1 day(s) ago')
  })
})
