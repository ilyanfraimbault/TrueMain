<script setup lang="ts">
// The player-scoped champion page's two empty states, which read differently
// because they end differently: a slice the reader emptied with a filter is
// undone by clearing it, a champion we hold no aggregate for at all can only be
// left for the global build. `filterLabels` decides which one this is — the
// page passes the filters it actually renders (patch, lane), so an empty list
// means nothing was narrowed and the champion itself is the blank one.
//
// No champion icon here: the header above the page's grid carries it in both
// states, and twice is once too many.
defineProps<{
  playerLabel: string
  championName: string | null
  /** Where "see the global build" goes — resolved by the page's slug map. */
  globalBuildPath: string
  filterLabels: string[]
}>()

defineEmits<{ clear: [] }>()
</script>

<template>
  <div class="flex flex-col items-center gap-3 surface rounded-lg px-6 py-8 text-center">
    <div class="space-y-1">
      <p class="text-sm font-medium text-default">
        {{ filterLabels.length ? 'Nothing on these filters' : 'No personal build breakdown yet' }}
      </p>
      <p class="text-sm text-muted">
        <template v-if="filterLabels.length">
          {{ playerLabel }} has no game on record for {{ filterLabels.join(' · ') }}.
          Pick another lane or patch, or clear the filters to go back to their main slice.
        </template>
        <template v-else>
          We don't have an aggregated build for {{ playerLabel }} on
          {{ championName ?? 'this champion' }} yet. Their recent games are below.
        </template>
      </p>
    </div>
    <div class="flex flex-wrap items-center justify-center gap-4">
      <UButton
        v-if="filterLabels.length"
        size="sm"
        color="neutral"
        variant="subtle"
        icon="i-lucide-filter-x"
        @click="$emit('clear')"
      >
        Clear filters
      </UButton>
      <NuxtLink
        :to="globalBuildPath"
        class="rounded text-sm text-primary transition-colors hover:text-primary/80 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
      >
        See the global build for {{ championName ?? 'this champion' }}
      </NuxtLink>
    </div>
  </div>
</template>
