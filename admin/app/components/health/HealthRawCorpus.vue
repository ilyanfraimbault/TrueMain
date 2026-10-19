<script setup lang="ts">
// Raw corpus (#1031): the measurements the signals were judged from, stated without a verdict.
//
// The two totals are the planner's row estimates (#2132) — the figure `/database` prints per
// table — because an exact participant count scanned the whole table and timed out the
// cockpit. They can be unavailable on their own: the card then says why, and the
// per-platform freshness below, which does not depend on them, still renders.
import type { RawDataFreshness } from '~~/shared/types/ops'
import { computed } from 'vue'
import { formatNumber, formatTimeAgo } from '~~/shared/utils/format'

const props = defineProps<{ rawData: RawDataFreshness }>()

const counts = computed(() => [
  { label: 'Matches', value: props.rawData.rawMatchCount },
  { label: 'Participants', value: props.rawData.rawParticipantCount },
])
</script>

<template>
  <UCard>
    <template #header>
      <div class="flex items-center justify-between gap-2">
        <p class="text-sm font-semibold text-highlighted">
          Raw corpus
        </p>
        <span class="text-xs text-muted tabular-nums">queue {{ rawData.queueId }}</span>
      </div>
    </template>

    <div class="grid grid-cols-2 gap-4 mb-1">
      <div v-for="count in counts" :key="count.label">
        <p class="text-xs text-muted uppercase">
          {{ count.label }}
        </p>
        <p class="mt-0.5 text-xl font-semibold text-highlighted tabular-nums">
          <template v-if="count.value !== null">
            ≈ {{ formatNumber(count.value) }}
          </template>
          <template v-else>
            —
          </template>
        </p>
      </div>
    </div>
    <p v-if="rawData.countsUnknownReason" class="mb-4 text-xs text-warning">
      Counts unavailable: {{ rawData.countsUnknownReason }}
    </p>
    <p v-else class="mb-4 text-xs text-dimmed">
      Planner row estimates, as on the Database page — not exact counts.
    </p>

    <p v-if="rawData.platforms.length === 0" class="text-sm text-dimmed italic">
      No match ingested for this queue yet.
    </p>
    <ul v-else class="space-y-1">
      <li
        v-for="platform in rawData.platforms"
        :key="platform.platformId"
        class="flex items-center justify-between gap-2 text-sm"
      >
        <span class="text-highlighted font-medium">{{ platform.platformId }}</span>
        <span class="text-xs text-muted tabular-nums">
          patch {{ platform.latestPatchVersion || '—' }} ·
          {{ platform.latestMatchStartAtUtc ? formatTimeAgo(platform.latestMatchStartAtUtc) : 'no match' }}
        </span>
      </li>
    </ul>
  </UCard>
</template>
