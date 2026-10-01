<script setup lang="ts">
/**
 * The running game. The gameflow phase opens this page on its own when a game
 * starts (`app.vue`), and the shell reads the game through its own API while
 * it runs (`useLiveGame`). Outside a game the page waits for the next one.
 *
 * In development a game can be played from a recorded tape without League —
 * `/dev/game-sim`, or `TRUEMAIN_LCU_REPLAY` (desktop/README.md).
 */
const { state, screen } = useLcuState()
const { game, syncedAt } = useLiveGame()

const waiting = computed(() => {
  if (screen.value === 'in-game') {
    return {
      title: 'Loading into the game',
      body: 'The board fills in as soon as the game has loaded.',
    }
  }
  return {
    title: 'Not in a game',
    body: state.value.connected
      ? 'This page opens on its own when your next game starts.'
      : 'Start League of Legends: this page follows you into your next game on its own.',
  }
})
</script>

<template>
  <GameScreen v-if="screen === 'in-game' && game" :game="game" :synced-at="syncedAt" />

  <div v-else class="relative flex h-full flex-col items-center justify-center gap-5 overflow-hidden px-8 text-center">
    <ChampionArt :alias="backdropAlias()" fade="vignette" position="60% 25%" class="opacity-40" />
    <span class="relative flex size-3 items-center justify-center">
      <span class="absolute inline-flex size-full animate-tm-pulse rounded-full bg-primary" />
      <span class="relative inline-flex size-1.5 rounded-full bg-primary" />
    </span>
    <div class="relative space-y-2">
      <h1 class="text-2xl font-semibold tracking-tight text-highlighted">{{ waiting.title }}</h1>
      <p class="max-w-sm text-sm text-muted">{{ waiting.body }}</p>
    </div>
  </div>
</template>
