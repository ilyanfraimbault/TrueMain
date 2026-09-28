<script setup lang="ts">
defineProps<{
  /**
   * When true, the player wasn't found at all (404 from the backend) rather
   * than just having no matches. Different copy so the user can tell whether
   * to double-check the spelling vs. just play more games.
   */
  notFound?: boolean
  /**
   * When true, a position/champion filter is active — the empty result is
   * likely the filter combination (e.g. a residual filter carried over in
   * the URL from another player), so point at clearing it rather than
   * implying the player has no tracked history at all.
   */
  filtered?: boolean
}>()
</script>

<template>
  <UEmpty
    :icon="notFound ? 'i-lucide-user-round-search' : filtered ? 'i-lucide-filter-x' : 'i-lucide-swords'"
    :title="notFound ? 'Player not found' : filtered ? 'No matches found' : 'No matches yet'"
  >
    <template #description>
      <template v-if="notFound">
        Check the spelling — the Riot ID should be <code class="whitespace-nowrap">GameName-TagLine</code>.
      </template>
      <template v-else-if="filtered">
        No tracked matches for the selected filters — try clearing them.
      </template>
      <template v-else>
        Their tracked match history is empty. Play a ranked game and check back later.
      </template>
    </template>
  </UEmpty>
</template>
