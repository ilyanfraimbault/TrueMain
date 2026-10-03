<script setup lang="ts">
/**
 * The running game. The gameflow phase opens this page on its own when a game
 * starts (`app.vue`), and the shell reads the game through its own API while
 * it runs (`useLiveGame`). Outside a game the page waits for the next one.
 *
 * In development a game can be played from a recorded tape without League —
 * `/dev/game-sim`, or `TRUEMAIN_LCU_REPLAY` (desktop/README.md).
 *
 * The in-game overlay is set up on its own page (`/overlay`), linked from
 * here: the page that shows the same panels in this window.
 */
const { state, screen } = useLcuState()
const { game, syncedAt } = useLiveGame()
const { view: loading } = useLoadingPlayers()
const { view: overlay } = useGameOverlay()

const overlayStatus = computed(() => {
  const view = overlay.value
  if (!view) return null
  if (!view.supported) return 'The in-game overlay works on macOS and Windows.'
  if (!view.settings.enabled) return 'The in-game overlay is off.'
  return `The overlay shows over your game; TAB adds the item value. ${view.shortcut} hides it.`
})

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
  <div class="h-full">
    <GameScreen v-if="screen === 'in-game' && game" :game="game" :synced-at="syncedAt">
      <template #actions>
        <UButton icon="i-lucide-layers" color="neutral" variant="ghost" size="sm" to="/overlay" aria-label="Overlay settings" title="Overlay settings" />
      </template>
    </GameScreen>

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
      <LoadingBoard v-if="screen === 'in-game' && loading.players.length" :players="loading.players" class="surface relative w-full max-w-xl rounded-xl p-3 text-left" />
      <div v-if="overlayStatus" class="relative mt-4 flex items-center gap-3 rounded-lg bg-elevated/60 py-1.5 pl-3 pr-1.5 ring-1 ring-default">
        <UIcon name="i-lucide-layers" class="size-4 text-primary" />
        <span class="text-xs text-muted">{{ overlayStatus }}</span>
        <UButton label="Customize" color="neutral" variant="ghost" size="xs" to="/overlay" />
      </div>
    </div>
  </div>
</template>
