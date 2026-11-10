import { mount } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { computed } from 'vue'
import type { OverviewReadModel } from '#site-shared/contract/api'
import AccountsCandidateDetail from '~~/app/components/accounts/AccountsCandidateDetail.vue'
import OverviewCandidatePipeline from '~~/app/components/overview/OverviewCandidatePipeline.vue'
import { DATA_QUALITY_ISSUE_META, DATA_QUALITY_ISSUE_ORDER } from '~/utils/data-quality'
import {
  CANDIDATE_STATUSES,
  candidateStatusColor,
  candidateStatusIcon,
  SEED_STATUSES,
  seedStatusColor,
  seedStatusIcon,
} from '~/utils/pipeline-status'
import { isTerminalSeedStatus } from '~/utils/seed-tracking'

// Each API vocabulary the admin draws, in the API's order, with a look for every value:
// a status the API adds must not render as an undressed badge.

describe('the admin\'s vocabularies', () => {
  it('reads the candidate statuses in pipeline order, each dressed', () => {
    expect(CANDIDATE_STATUSES).toEqual(['New', 'Scored', 'Queued', 'Processing', 'Validated'])
    for (const status of CANDIDATE_STATUSES) {
      expect(candidateStatusColor(status)).toBeTruthy()
      expect(candidateStatusIcon(status)).toMatch(/^i-lucide-/)
    }
  })

  it('reads the seed statuses in lifecycle order, each dressed, the settled ones terminal', () => {
    expect(SEED_STATUSES).toEqual(['Pending', 'Resolving', 'Ingested', 'Failed'])
    for (const status of SEED_STATUSES) {
      expect(seedStatusColor(status)).toBeTruthy()
      expect(seedStatusIcon(status)).toMatch(/^i-lucide-/)
    }
    expect(SEED_STATUSES.filter(isTerminalSeedStatus)).toEqual(['Ingested', 'Failed'])
  })

  it('lists every data-quality check, each with its words', () => {
    expect(DATA_QUALITY_ISSUE_ORDER).toHaveLength(Object.keys(DATA_QUALITY_ISSUE_META).length)
    for (const issue of DATA_QUALITY_ISSUE_ORDER) {
      expect(DATA_QUALITY_ISSUE_META[issue].label).toBeTruthy()
    }
  })
})

describe('OverviewCandidatePipeline', () => {
  beforeEach(() => {
    vi.stubGlobal('computed', computed)
    vi.stubGlobal('CHART_PRIMARY', '#000')
    vi.stubGlobal('barChartHeight', () => 200)
    vi.stubGlobal('indexLabelFormatter', () => () => '')
    vi.stubGlobal('CANDIDATE_STATUSES', CANDIDATE_STATUSES)
    vi.stubGlobal('candidateStatusColor', candidateStatusColor)
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('draws one bucket per candidate status, in pipeline order, coloured by status', () => {
    const stats = { candidatesByStatus: { New: 1, Scored: 2, Queued: 3, Processing: 0, Validated: 4 } } as unknown as OverviewReadModel
    const wrapper = mount(OverviewCandidatePipeline, {
      props: { stats, pending: false },
      global: {
        mocks: { horizontalBarProps: () => ({}) },
        stubs: {
          UCard: { template: '<div><slot name="header" /><slot /></div>' },
          PanelTitle: true,
          USkeleton: true,
          UBadge: { props: ['color', 'label'], template: '<span class="badge" :data-color="color">{{ label }}<slot /></span>' },
          BarChart: true,
        },
      },
    })

    const badges = wrapper.findAll('.badge').slice(1)
    expect(badges.map(badge => badge.text())).toEqual(['New: 1', 'Scored: 2', 'Queued: 3', 'Processing: 0', 'Validated: 4'])
    expect(badges[0]!.attributes('data-color')).toBe('neutral')
    expect(badges[4]!.attributes('data-color')).toBe('success')
  })
})

describe('AccountsCandidateDetail', () => {
  beforeEach(() => {
    vi.stubGlobal('computed', computed)
    vi.stubGlobal('CANDIDATE_STATUSES', CANDIDATE_STATUSES)
    vi.stubGlobal('useChampionStatic', () => ({ nameFor: () => 'Ahri', iconFor: () => null }))
    vi.stubGlobal('candidateRiotIdLabel', (gameName: string, tagLine: string) => `${gameName}#${tagLine}`)
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('lights the pipeline stages up to the candidate\'s own', () => {
    const wrapper = mount(AccountsCandidateDetail, {
      props: {
        open: true,
        pending: false,
        error: null,
        errorTraceId: undefined,
        detail: { status: 'Queued', championId: 103, gameName: 'Faker', tagLine: 'KR1', platformId: 'KR', puuid: 'p', score: 0.5, championPoints: 1, championRankInMasteryTop: 1, discoveredAtUtc: '2026-11-01T00:00:00Z', scoredAtUtc: null, queuedAtUtc: null, validatedAtUtc: null, ingestedMatchCount: 0, seedRequest: null } as never,
      },
      global: {
        mocks: { candidateStatusColor, candidateRiotIdLabel: (gameName: string, tagLine: string) => `${gameName}#${tagLine}`, candidateStatusIcon, seedStatusColor, seedStatusIcon },
        stubs: {
          USlideover: { template: '<div><slot name="body" /></div>' },
          UBadge: { props: ['label', 'variant'], template: '<span class="stage" :data-variant="variant">{{ label }}</span>' },
          UIcon: true,
          USkeleton: true,
          FetchErrorAlert: true,
          NuxtImg: true,
          SiteLink: true,
          UAlert: true,
        },
      },
    })

    const stages = wrapper.findAll('ol .stage')
    expect(stages.map(stage => stage.text())).toEqual(['New', 'Scored', 'Queued', 'Processing', 'Validated'])
    expect(stages.map(stage => stage.attributes('data-variant'))).toEqual(['subtle', 'subtle', 'subtle', 'soft', 'soft'])
  })
})
