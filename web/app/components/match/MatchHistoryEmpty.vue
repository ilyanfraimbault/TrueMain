<script setup lang="ts">
defineProps<{
  /**
   * When true, the player wasn't found at all (404 from the backend) rather
   * than just having no matches. Different copy so the user can tell whether
   * to double-check the spelling vs. just play more games.
   */
  notFound?: boolean
}>()
</script>

<template>
  <div class="glass rounded-lg px-6 py-12 text-center">
    <p class="text-base font-semibold">
      {{ notFound ? 'Player not found' : 'No matches yet' }}
    </p>
    <p class="mt-1 text-sm text-muted">
      <template v-if="notFound">
        Check the spelling — the Riot ID should be <code>GameName-TagLine</code>.
      </template>
      <template v-else>
        Their tracked match history is empty. Play a ranked game and check back later.
      </template>
    </p>
  </div>
</template>
