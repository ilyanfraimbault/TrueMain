<script setup lang="ts">
// The body of the per-champion aggregate-freshness slide-over — the one heavy
// measurement, loaded on an explicit click rather than with the panel.
//
// Extracted from pages/data-quality.vue (#992) alongside the match detail, for
// the same reason: the page should read as its own structure.
import type { AggregateFreshnessResponse } from '~~/shared/types/ops'

defineProps<{
  freshness: AggregateFreshnessResponse | null
  pending: boolean
  error: string | null
}>()

const { nameFor, iconFor } = useChampionStatic()

function formatAge(ageHours: number): string {
  return ageHours < 48
    ? `${ageHours.toFixed(1)} h ago`
    : `${(ageHours / 24).toFixed(1)} d ago`
}
</script>

<template>
  <div v-if="pending" class="space-y-3">
    <USkeleton v-for="n in 6" :key="n" class="h-10 w-full" />
  </div>

  <UAlert
    v-else-if="error"
    color="error"
    variant="subtle"
    icon="i-lucide-triangle-alert"
    title="Failed to load"
    :description="error"
  />

  <div v-else-if="freshness" class="space-y-4">
    <div class="flex flex-wrap items-center gap-2">
      <UBadge
        :color="freshness.staleChampionCount === 0 ? 'success' : 'warning'"
        variant="subtle"
        :label="`${freshness.staleChampionCount} of ${freshness.championCount} stale`"
      />
      <span class="text-xs text-dimmed">
        Stale after {{ freshness.staleAfterHours }} h · patches
        {{ freshness.patches.join(', ') || '—' }}
      </span>
    </div>

    <p v-if="freshness.champions.length === 0" class="text-sm text-muted">
      No aggregate rows on the covered patches yet.
    </p>

    <ul v-else class="divide-default divide-y">
      <li
        v-for="row in freshness.champions"
        :key="`${row.championId}-${row.patch}`"
        class="flex items-center justify-between gap-3 py-2"
      >
        <div class="flex items-center gap-2 min-w-0">
          <img
            v-if="iconFor(row.championId)"
            :src="iconFor(row.championId)!"
            :alt="nameFor(row.championId)"
            class="size-6 rounded"
            loading="lazy"
          >
          <div class="min-w-0">
            <p class="text-xs text-highlighted truncate">
              {{ nameFor(row.championId) }}
            </p>
            <p class="text-xs text-dimmed">
              {{ row.patch }} · {{ row.scopeRows }} scope row(s)
            </p>
          </div>
        </div>
        <span
          class="text-xs whitespace-nowrap tabular-nums"
          :class="row.status === 'red'
            ? 'text-error'
            : row.status === 'amber' ? 'text-warning' : 'text-muted'"
        >
          {{ formatAge(row.ageHours) }}
        </span>
      </li>
    </ul>
  </div>
</template>
