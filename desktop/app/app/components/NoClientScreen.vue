<script setup lang="ts">
import { backdropAlias } from '~/composables/useChampionStatics'

const alias = backdropAlias()
</script>

<template>
  <div class="relative flex h-full flex-col overflow-hidden">
    <ChampionArt :alias="alias" fade="vignette" position="60% 25%" />

    <header class="relative p-6">
      <AppWordmark class="text-lg" />
    </header>

    <div class="relative flex flex-1 flex-col items-center justify-center gap-5 px-8 pb-16 text-center">
      <span class="relative flex size-3 items-center justify-center">
        <span class="absolute inline-flex size-full animate-tm-pulse rounded-full bg-primary" />
        <span class="relative inline-flex size-1.5 rounded-full bg-primary" />
      </span>
      <div class="space-y-2">
        <h1 class="text-2xl font-semibold tracking-tight text-highlighted">
          Waiting for the League client
        </h1>
        <p class="max-w-sm text-sm text-muted">
          Start League of Legends and this window follows you into champion select on its own.
        </p>
      </div>
    </div>
  </div>
</template>
