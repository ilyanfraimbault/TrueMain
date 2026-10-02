<script setup lang="ts">
// One ranked list of the Desktop app page (#1805) — versions, operating systems,
// pages, features: a label, its figure and a bar scaled to the list's largest, so
// the ranking reads at a glance and the exact number is still printed.

import type { DesktopShareRow } from '~~/shared/types/desktop'
import { formatNumber } from '~~/shared/utils/format'

const props = defineProps<{
  title: string
  info?: string
  rows: DesktopShareRow[]
  emptyMessage: string
}>()

const max = computed(() => Math.max(1, ...props.rows.map(row => row.value)))
</script>

<template>
  <UCard>
    <template #header>
      <PanelTitle :title="title" :info="info" />
    </template>

    <p v-if="rows.length === 0" class="text-sm text-muted">
      {{ emptyMessage }}
    </p>
    <ul v-else class="space-y-2.5">
      <li v-for="row in rows" :key="row.key">
        <div class="flex items-baseline justify-between gap-3 text-sm">
          <span class="truncate" :class="row.value > 0 ? 'text-highlighted' : 'text-dimmed'">{{ row.label }}</span>
          <span class="shrink-0 tabular-nums text-highlighted">
            {{ formatNumber(row.value) }}
            <span v-if="row.hint" class="ml-1 text-xs text-dimmed">{{ row.hint }}</span>
          </span>
        </div>
        <div class="mt-1 h-1 rounded-full bg-elevated">
          <div
            class="h-1 rounded-full bg-primary"
            :style="{ width: `${(row.value / max) * 100}%` }"
          />
        </div>
      </li>
    </ul>
  </UCard>
</template>
