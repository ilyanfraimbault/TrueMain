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
  <UEmpty
    :icon="filterLabels.length ? 'i-lucide-filter-x' : 'i-lucide-hammer'"
    :title="filterLabels.length ? 'Nothing on these filters' : 'No personal build breakdown yet'"
  >
    <template #description>
      <template v-if="filterLabels.length">
        {{ playerLabel }} has no game on record for {{ filterLabels.join(' · ') }}.
        Pick another lane or patch, or clear the filters to go back to their main slice.
      </template>
      <template v-else>
        We don't have an aggregated build for {{ playerLabel }} on
        {{ championName ?? 'this champion' }} yet. Their recent games are below.
      </template>
    </template>

    <template #actions>
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
    </template>
  </UEmpty>
</template>
