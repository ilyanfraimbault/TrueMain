<script setup lang="ts">
// Alerts (#2292): what the pipeline-health alerter told the operator, under the verdict it
// watches. It is off until a Discord webhook is configured, and says so in place — an empty
// list must not pass for "nothing went wrong" when nothing was listening.
import type { HealthAlerts } from '~~/shared/types/ops'
import { computed } from 'vue'
import { formatDateTime, formatTimeAgo } from '~~/shared/utils/format'
import { healthAlertBadge, healthAlertingRules, healthAlertKeyLabel } from '~~/shared/utils/health-alerts'

const props = defineProps<{ alerts: HealthAlerts | null, pending: boolean }>()

const rules = computed(() => (props.alerts ? healthAlertingRules(props.alerts) : ''))
const openIncidents = computed(() => (props.alerts?.openIncidents ?? []).map(healthAlertKeyLabel))
</script>

<template>
  <UCard>
    <template #header>
      <div class="flex items-center justify-between gap-2">
        <p class="text-sm font-semibold text-highlighted">
          Alerts
        </p>
        <span v-if="alerts?.enabled" class="text-xs text-muted">
          sent to {{ alerts.channel }}
          <template v-if="alerts.lastEvaluatedAtUtc">
            · checked {{ formatTimeAgo(alerts.lastEvaluatedAtUtc) }}
          </template>
        </span>
      </div>
    </template>

    <USkeleton v-if="pending && !alerts" class="h-12 w-full" />
    <p v-else-if="!alerts" class="text-sm text-dimmed italic">
      Alerts could not be read.
    </p>
    <template v-else>
      <p v-if="!alerts.enabled" class="text-sm text-warning">
        Alerting is off: no Discord webhook is configured
        (<span class="font-mono">Alerting:DiscordWebhookUrl</span>). A red verdict is only
        seen by whoever opens this page.
      </p>
      <p v-else class="text-xs text-dimmed">
        {{ rules }}
      </p>
      <p v-if="openIncidents.length > 0" class="mt-2 text-sm text-error">
        Open: {{ openIncidents.join(', ') }}
      </p>

      <p v-if="alerts.alerts.length === 0" class="mt-3 text-sm text-muted">
        No alert sent yet.
      </p>
      <ul v-else class="mt-3 divide-y divide-default">
        <li
          v-for="alert in alerts.alerts"
          :key="alert.id"
          class="py-2 first:pt-0 last:pb-0"
        >
          <div class="flex flex-wrap items-center gap-x-3 gap-y-1">
            <UBadge
              :color="healthAlertBadge(alert).color"
              variant="subtle"
              :label="healthAlertBadge(alert).label"
              class="shrink-0"
            />
            <span class="text-sm text-highlighted font-medium grow min-w-0 truncate">{{ alert.title }}</span>
            <span class="text-xs text-muted tabular-nums" :title="formatDateTime(alert.sentAtUtc)">
              {{ formatTimeAgo(alert.sentAtUtc) }}
            </span>
          </div>
          <p v-if="alert.error" class="mt-1 text-xs text-warning">
            {{ alert.error }}
          </p>
          <p
            v-for="change in alert.changes"
            :key="change.key"
            class="mt-1 text-xs text-muted"
          >
            <span class="text-default">{{ change.title }}</span> {{ change.change }} — {{ change.headline }}
          </p>
        </li>
      </ul>
    </template>
  </UCard>
</template>
