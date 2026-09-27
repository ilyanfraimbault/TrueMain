<script setup lang="ts">
/**
 * The live draft. The gameflow phase opens this page on its own when champion
 * select starts (`app.vue`); outside of it the page says so and points at the
 * simulator, where a draft can be played through by hand.
 */
const { state, screen } = useLcuState()
</script>

<template>
  <DraftScreen v-if="screen === 'draft' && state.draft" :draft="state.draft" />

  <div v-else class="relative flex h-full flex-col items-center justify-center gap-5 overflow-hidden px-8 text-center">
    <ChampionArt :alias="backdropAlias()" fade="vignette" position="60% 25%" class="opacity-40" />
    <span class="relative flex size-3 items-center justify-center">
      <span class="absolute inline-flex size-full animate-tm-pulse rounded-full bg-primary" />
      <span class="relative inline-flex size-1.5 rounded-full bg-primary" />
    </span>
    <div class="relative space-y-2">
      <h1 class="text-2xl font-semibold tracking-tight text-highlighted">Not in champion select</h1>
      <p class="max-w-sm text-sm text-muted">
        {{ state.connected
          ? 'This page opens on its own when your next champion select starts.'
          : 'Start League of Legends: this page follows you into champion select on its own.' }}
      </p>
    </div>
    <UButton to="/simulator" icon="i-lucide-flask-conical" label="Try a draft in the simulator" color="neutral" variant="subtle" class="relative" />
  </div>
</template>
