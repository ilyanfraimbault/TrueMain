import type { NavigationMenuItem } from '@nuxt/ui'
import type { NavSummary } from '~~/shared/types/ops'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { computed, ref } from 'vue'
import { NAV_SUMMARY_REFRESH_MS, useNavSummary } from '~~/app/composables/useNavSummary'
import { navBadges, withNavBadges } from '~/utils/nav-badges'

// The sidebar's badges (#2294): a count on Known players, a dot on Health in the
// verdict's colour, nothing for a zero, a green verdict, an unmeasured one, or a
// failed fetch.

function summary(overrides: Partial<NavSummary> = {}): NavSummary {
  return {
    openConflicts: 0,
    homonymGroups: 0,
    countedAtUtc: '2026-10-26T08:00:00Z',
    healthStatus: 'green',
    healthEvaluatedAtUtc: '2026-10-26T08:00:00Z',
    ...overrides,
  }
}

describe('navBadges', () => {
  it('shows nothing without a summary', () => {
    expect(navBadges(null)).toEqual({})
    expect(navBadges(undefined)).toEqual({})
  })

  it('shows nothing when the queues are empty and the verdict is green', () => {
    expect(navBadges(summary())).toEqual({})
  })

  it('counts open conflicts and homonym groups on Known players', () => {
    const badge = navBadges(summary({ openConflicts: 2, homonymGroups: 3 }))['/known-players']
    expect(badge?.label).toBe(5)
    expect(badge?.['aria-label']).toBe('2 open conflicts, 3 homonym groups')
    expect(navBadges(summary({ homonymGroups: 1 }))['/known-players']?.title).toBe('0 open conflicts, 1 homonym group')
  })

  it('paints the Health dot in the verdict colour', () => {
    expect(navBadges(summary({ healthStatus: 'red' }))['/health']?.class).toContain('bg-error')
    expect(navBadges(summary({ healthStatus: 'amber' }))['/health']?.class).toContain('bg-warning')
    expect(navBadges(summary({ healthStatus: 'amber' }))['/health']?.title).toBe('Pipeline health: needs attention')
  })

  it('shows a measured unknown, but not a verdict nobody evaluated', () => {
    expect(navBadges(summary({ healthStatus: 'unknown' }))['/health']?.class).toContain('bg-neutral')
    expect(navBadges(summary({ healthStatus: 'unknown', healthEvaluatedAtUtc: null }))['/health']).toBeUndefined()
  })
})

describe('withNavBadges', () => {
  it('puts each badge on the link of its route and leaves the rest alone', () => {
    const groups: NavigationMenuItem[][] = [[
      { label: 'Monitor', type: 'label' },
      { label: 'Health', to: '/health' },
      { label: 'Logs', to: '/logs' },
    ]]
    const decorated = withNavBadges(groups, navBadges(summary({ healthStatus: 'red' })))
    expect(decorated[0]![1]!.badge).toMatchObject({ title: 'Pipeline health: failing' })
    expect(decorated[0]![0]).toBe(groups[0]![0])
    expect(decorated[0]![2]).toBe(groups[0]![2])
  })
})

describe('useNavSummary', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  function setup() {
    const data = ref<NavSummary | undefined>(summary({ openConflicts: 1, healthStatus: 'amber' }))
    const error = ref<Error | null>(null)
    const fetched = { data, error, refresh: vi.fn() }
    const useOps = vi.fn(() => fetched)
    const useLiveRefresh = vi.fn()
    vi.stubGlobal('useOps', useOps)
    vi.stubGlobal('useLiveRefresh', useLiveRefresh)
    vi.stubGlobal('computed', computed)
    return { data, error, fetched, useOps, useLiveRefresh }
  }

  it('reads the endpoint and refreshes it every minute', () => {
    const { fetched, useOps, useLiveRefresh } = setup()
    const { summary: current, healthStatus } = useNavSummary()

    expect(useOps).toHaveBeenCalledWith('/nav-summary')
    expect(useLiveRefresh).toHaveBeenCalledWith(fetched, { every: NAV_SUMMARY_REFRESH_MS })
    expect(current.value?.openConflicts).toBe(1)
    expect(healthStatus.value).toBe('amber')
  })

  it('reads null after a failed fetch, whatever payload is kept', () => {
    const { error, data } = setup()
    const { summary: current, healthStatus } = useNavSummary()

    error.value = new Error('502')
    expect(current.value).toBeNull()
    expect(healthStatus.value).toBeNull()

    error.value = null
    data.value = undefined
    expect(current.value).toBeNull()
  })
})
