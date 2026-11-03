import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import type { HealthAlert, HealthAlerts } from '~~/shared/types/ops'
import HealthAlertsPanel from '~~/app/components/health/HealthAlertsPanel.vue'
import { healthAlertBadge, healthAlertingRules, healthAlertKeyLabel } from '~~/shared/utils/health-alerts'

// The /health alerts panel (#2292): an alerter with no webhook says it is off instead of
// showing an empty list, and each alert names what changed and whether it was delivered.
const global = {
  stubs: {
    UCard: { template: '<div><slot name="header" /><slot /></div>' },
    UBadge: { props: ['label'], template: '<span>{{ label }}</span>' },
    USkeleton: { template: '<div class="skeleton" />' },
  },
}

function alert(overrides: Partial<HealthAlert> = {}): HealthAlert {
  return {
    id: 'a1',
    sentAtUtc: '2026-10-26T08:00:00Z',
    kind: 'failing',
    title: '[preprod] Pipeline health: 1 signal failing',
    verdict: 'red',
    channel: 'discord',
    delivered: true,
    error: null,
    changes: [{ key: 'dataQuality', title: 'Data quality', change: 'failing', status: 'red', headline: '1 detector is red.' }],
    ...overrides,
  }
}

function payload(overrides: Partial<HealthAlerts> = {}): HealthAlerts {
  return {
    enabled: true,
    channel: 'discord',
    evaluationIntervalSeconds: 300,
    redConfirmations: 2,
    cooldownMinutes: 60,
    processFailureThreshold: 3,
    lastEvaluatedAtUtc: '2026-10-26T08:05:00Z',
    openIncidents: [],
    alerts: [],
    ...overrides,
  }
}

describe('healthAlertBadge', () => {
  it('reads an undelivered alert as such, whatever its kind', () => {
    expect(healthAlertBadge({ kind: 'failing', delivered: false }).label).toBe('Not delivered')
    expect(healthAlertBadge({ kind: 'failing', delivered: true })).toEqual({ label: 'Failing', color: 'error' })
    expect(healthAlertBadge({ kind: 'recovered', delivered: true })).toEqual({ label: 'Recovered', color: 'success' })
  })
})

describe('healthAlertingRules', () => {
  it('states the cadence, the debounce, the streak and the cooldown', () => {
    const text = healthAlertingRules(payload())
    expect(text).toContain('every 5 min')
    expect(text).toContain('after 2 red evaluations in a row')
    expect(text).toContain('3 failed runs')
    expect(text).toContain('once per 60 min')
  })

  it('falls back to seconds and to a single confirmation', () => {
    const text = healthAlertingRules(payload({ evaluationIntervalSeconds: 10, redConfirmations: 1 }))
    expect(text).toContain('every 10 s')
    expect(text).toContain('on its first red evaluation')
  })
})

describe('healthAlertKeyLabel', () => {
  it('drops the process prefix', () => {
    expect(healthAlertKeyLabel('process:MatchIngestion')).toBe('MatchIngestion')
    expect(healthAlertKeyLabel('dataQuality')).toBe('dataQuality')
  })
})

describe('HealthAlertsPanel', () => {
  it('says alerting is off when no webhook is configured', () => {
    const wrapper = mount(HealthAlertsPanel, { props: { alerts: payload({ enabled: false, channel: null }), pending: false }, global })
    expect(wrapper.text()).toContain('Alerting is off')
    expect(wrapper.text()).toContain('Alerting:DiscordWebhookUrl')
    expect(wrapper.text()).toContain('No alert sent yet.')
  })

  it('lists the alerts with what changed, and the open incidents', () => {
    const wrapper = mount(HealthAlertsPanel, {
      props: {
        alerts: payload({
          openIncidents: ['process:MatchIngestion'],
          alerts: [alert(), alert({ id: 'a2', delivered: false, error: 'HTTP 429' })],
        }),
        pending: false,
      },
      global,
    })
    expect(wrapper.text()).toContain('sent to discord')
    expect(wrapper.text()).toContain('Open: MatchIngestion')
    expect(wrapper.text()).toContain('[preprod] Pipeline health: 1 signal failing')
    expect(wrapper.text()).toContain('Data quality failing — 1 detector is red.')
    expect(wrapper.text()).toContain('Not delivered')
    expect(wrapper.text()).toContain('HTTP 429')
  })

  it('shows a skeleton while loading and a sentence when the read failed', () => {
    expect(mount(HealthAlertsPanel, { props: { alerts: null, pending: true }, global }).find('.skeleton').exists()).toBe(true)
    expect(mount(HealthAlertsPanel, { props: { alerts: null, pending: false }, global }).text())
      .toContain('Alerts could not be read.')
  })
})
