<script setup lang="ts">
import type { AppState } from '~/types/lcu'

defineProps<{ state: AppState }>()
</script>

<template>
  <div class="flex h-full flex-col gap-6 p-6">
    <header class="flex items-baseline justify-between gap-4">
      <div>
        <p class="text-xs uppercase tracking-wide text-dimmed">Signed in as</p>
        <h1 class="text-xl font-semibold text-highlighted">
          {{ state.riotId ?? 'Unknown player' }}
        </h1>
      </div>
      <UBadge variant="subtle" color="neutral">{{ state.phase }}</UBadge>
    </header>

    <!--
      The dashboard's real content (recent form, last games, per-champion
      performance) is #1683 and needs the player-identity work in #1682 first:
      our database holds true mains only, so there is nothing to draw for a
      player we do not track yet. Stating that beats an empty shell that reads
      as a bug.
    -->
    <UAlert
      icon="i-lucide-construction"
      color="neutral"
      variant="subtle"
      title="Your dashboard is not built yet"
      description="Recent form, last games and per-champion performance land with the player dashboard work. Champion select already works — queue up and this window switches to it."
    />
  </div>
</template>
