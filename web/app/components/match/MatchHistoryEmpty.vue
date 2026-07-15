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
  <div class="glass rounded-lg px-6 py-12 text-center">
    <p class="text-base font-semibold">
      {{ notFound ? 'Player not found' : filtered ? 'No matches found' : 'No matches yet' }}
    </p>
    <p class="mt-1 text-sm text-muted">
      <template v-if="notFound">
        Check the spelling — the Riot ID should be <code>GameName-TagLine</code>.
      </template>
      <template v-else-if="filtered">
        No tracked matches for the selected filters — try clearing them.
      </template>
      <template v-else>
        Their tracked match history is empty. Play a ranked game and check back later.
      </template>
    </p>
  </div>
</template>
