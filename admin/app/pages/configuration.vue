<script setup lang="ts">
// Effective configuration viewer (#1034): what each host is actually running
// with, as bound at boot — not as parsed from a compose file that may not match.
//
// Two processes, each grouped into its allow-listed sections. The Api's half is
// built live on every request (it can introspect its own container); the
// Ingestor's is a snapshot published to Mongo at its own boot, so its "as of" can
// legitimately be older than the page load without meaning anything is stale.
//
// Read-only: this page has no write path, by design (see the issue's scope).
import type {
  EffectiveConfigurationOrigin,
  EffectiveConfigurationProcess,
  EffectiveConfigurationValue,
} from '~~/shared/types/ops'
import { formatDateTime, formatTimeAgo } from '~~/shared/utils/format'

const { data, pending, error, refresh } = useEffectiveConfiguration()

const processes = computed<EffectiveConfigurationProcess[]>(() => data.value?.processes ?? [])

// Stated in words, not just colour: an operator scanning for "did someone
// override this" should not have to learn a colour code first.
const ORIGIN_META: Record<EffectiveConfigurationOrigin, { label: string, color: 'neutral' | 'primary' | 'warning' }> = {
  default: { label: 'Default', color: 'neutral' },
  override: { label: 'Override', color: 'primary' },
  derived: { label: 'Derived', color: 'warning' },
}

function originLabel(value: EffectiveConfigurationValue): string {
  const meta = ORIGIN_META[value.origin]
  return value.source ? `${meta.label} — ${value.source}` : meta.label
}
</script>

<template>
  <UDashboardPanel id="configuration">
    <template #header>
      <UDashboardNavbar title="Configuration" icon="i-lucide-settings">
        <template #leading>
          <UDashboardSidebarCollapse />
        </template>
        <template #right>
          <UButton
            icon="i-lucide-refresh-cw"
            color="neutral"
            variant="ghost"
            :loading="pending"
            aria-label="Refresh"
            @click="refresh()"
          />
        </template>
      </UDashboardNavbar>
    </template>

    <template #body>
      <FetchErrorAlert
        v-if="error"
        :error="error"
        title="Failed to load the effective configuration"
        class="mb-6"
      />

      <div v-if="pending && processes.length === 0" class="space-y-6">
        <USkeleton class="h-24 w-full" />
        <USkeleton class="h-24 w-full" />
      </div>

      <div v-for="process in processes" :key="process.processName" class="mb-8 last:mb-0">
        <div class="mb-3 flex flex-wrap items-center gap-2">
          <h2 class="text-lg font-semibold text-highlighted">
            {{ process.processName }}
          </h2>
          <UBadge color="neutral" variant="subtle" :label="process.environment" />
          <UBadge v-if="process.version" color="neutral" variant="outline" :label="process.version" />
          <span
            class="text-xs text-dimmed"
            :title="formatDateTime(process.capturedAtUtc)"
          >
            as of {{ formatTimeAgo(process.capturedAtUtc) }}
          </span>
        </div>

        <div class="grid grid-cols-1 gap-4 lg:grid-cols-2">
          <UCard v-for="section in process.sections" :key="section.name">
            <template #header>
              <p class="text-sm font-medium text-highlighted">
                {{ section.title }}
              </p>
              <p class="mt-0.5 text-xs text-muted">
                {{ section.description }}
              </p>
            </template>

            <ul class="divide-y divide-default">
              <li
                v-for="value in section.values"
                :key="value.key"
                class="flex flex-col gap-1 py-2 first:pt-0 last:pb-0"
              >
                <div class="flex items-start justify-between gap-3">
                  <span class="font-mono text-xs text-highlighted">{{ value.name }}</span>
                  <span class="flex shrink-0 items-baseline gap-2 text-right">
                    <span class="text-sm tabular-nums text-highlighted">
                      {{ value.valueLabel ?? value.value ?? 'not set' }}
                    </span>
                    <span
                      v-if="value.valueLabel && value.value"
                      class="font-mono text-xs text-dimmed"
                    >({{ value.value }})</span>
                  </span>
                </div>
                <div class="flex items-center justify-between gap-3">
                  <span
                    v-if="value.notice"
                    class="flex items-center gap-1 text-xs text-warning"
                  >
                    <UIcon name="i-lucide-triangle-alert" class="size-3.5 shrink-0" />
                    {{ value.notice }}
                  </span>
                  <span v-else />
                  <UBadge
                    :color="ORIGIN_META[value.origin].color"
                    variant="subtle"
                    size="sm"
                    :label="originLabel(value)"
                  />
                </div>
              </li>
            </ul>
          </UCard>
        </div>
      </div>

      <p v-if="!pending && processes.length === 0 && !error" class="text-sm text-muted">
        No process has published its configuration yet.
      </p>
    </template>
  </UDashboardPanel>
</template>
