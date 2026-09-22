<script setup lang="ts">
import type { AppState } from '~/types/lcu'
import { backdropAlias } from '~/composables/useChampionStatics'

defineProps<{ state: AppState }>()

const alias = backdropAlias()

/** What the dashboard will hold — #1683, once the player identity work in #1682 lands. */
const upcoming = [
  { icon: 'i-lucide-trending-up', title: 'Recent form', text: 'How your last games went, lane by lane.' },
  { icon: 'i-lucide-swords', title: 'Your champions', text: 'Win rate and matchups on what you actually play.' },
  { icon: 'i-lucide-history', title: 'Last games', text: 'Each one against what the numbers expected.' },
]
</script>

<template>
  <div class="flex h-full flex-col overflow-y-auto">
    <header class="relative h-44 shrink-0 overflow-hidden">
      <ChampionArt :alias="alias" fade="x" position="75% 20%" />
      <div class="relative flex h-full flex-col justify-between p-6">
        <AppWordmark class="text-base" />
        <div class="flex items-end justify-between gap-4">
          <div>
            <p class="text-[11px] font-medium uppercase tracking-[0.14em] text-dimmed">Signed in as</p>
            <h1 class="mt-1 text-3xl font-semibold tracking-tight text-highlighted">
              {{ state.riotId ?? 'Unknown player' }}
            </h1>
          </div>
          <UBadge color="neutral" size="sm" class="mb-1 tabular-nums">
            {{ state.phase }}
          </UBadge>
        </div>
      </div>
    </header>

    <!--
      The dashboard's real content is #1683 and needs the player identity work in
      #1682 first: our database holds true mains only, so there is nothing to draw
      for a player we do not track yet. Saying what is coming beats an empty shell
      that reads as a bug.
    -->
    <section class="flex flex-1 flex-col gap-5 p-6">
      <div class="flex items-baseline justify-between">
        <h2 class="text-[11px] font-medium uppercase tracking-[0.14em] text-dimmed">Dashboard</h2>
        <p class="text-xs text-muted">Queue up — this window switches to champion select on its own.</p>
      </div>

      <ul class="grid grid-cols-3 gap-3">
        <li v-for="item in upcoming" :key="item.title" class="surface rounded-xl p-4">
          <UIcon :name="item.icon" class="size-5 text-primary" />
          <p class="mt-3 text-sm font-semibold text-highlighted">{{ item.title }}</p>
          <p class="mt-1 text-xs leading-relaxed text-muted">{{ item.text }}</p>
          <p class="mt-3 text-[11px] font-medium uppercase tracking-wide text-dimmed">Not built yet</p>
        </li>
      </ul>
    </section>
  </div>
</template>
