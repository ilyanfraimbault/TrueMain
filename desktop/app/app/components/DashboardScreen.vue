<script setup lang="ts">
import type { AppState, GameflowPhase } from '~/types/lcu'
import { backdropAlias } from '~/composables/useChampionStatics'

const props = defineProps<{ state: AppState }>()

const alias = backdropAlias()
const { profileIconOf } = useChampionStatics()

const name = computed(() => props.state.riotId?.split('#')[0] ?? null)
const tag = computed(() => props.state.riotId?.split('#')[1] ?? null)
const icon = computed(() => (props.state.profileIconId !== null ? profileIconOf(props.state.profileIconId) : null))

/** The client's phase in the player's words. `live` phases pulse. */
const STATUS: Partial<Record<GameflowPhase, { label: string, live?: boolean }>> = {
  Lobby: { label: 'In lobby' },
  Matchmaking: { label: 'In queue', live: true },
  ReadyCheck: { label: 'Match found', live: true },
  InProgress: { label: 'In game', live: true },
  Reconnect: { label: 'Reconnecting', live: true },
  WaitingForStats: { label: 'Post-game' },
  PreEndOfGame: { label: 'Post-game' },
  EndOfGame: { label: 'Post-game' },
}
const status = computed(() => STATUS[props.state.phase] ?? { label: 'Online' })
</script>

<template>
  <div class="relative flex h-full flex-col overflow-hidden">
    <ChampionArt :alias="alias" fade="vignette" position="70% 20%" />

    <header class="relative flex items-center justify-between px-6 py-5">
      <AppWordmark class="text-base" />
      <div class="flex items-center gap-2 rounded-full border border-default bg-ink-950/60 px-3 py-1.5 text-sm backdrop-blur">
        <span class="relative flex size-2">
          <span v-if="status.live" class="absolute inline-flex size-full animate-ping rounded-full bg-primary opacity-60" />
          <span class="relative inline-flex size-2 rounded-full" :class="status.live ? 'bg-primary' : 'bg-success'" />
        </span>
        <span class="font-medium text-highlighted">{{ status.label }}</span>
      </div>
    </header>

    <section class="relative mt-auto flex items-end gap-5 p-8">
      <div class="relative shrink-0">
        <div class="size-24 overflow-hidden rounded-2xl bg-ink-900 ring-2 ring-primary/60 shadow-lg">
          <img v-if="icon" :src="icon" alt="" class="size-full object-cover">
        </div>
        <span
          v-if="state.summonerLevel !== null"
          class="absolute -bottom-2 left-1/2 -translate-x-1/2 rounded-full border border-default bg-ink-950 px-2 py-0.5 text-xs font-semibold tabular-nums text-highlighted"
        >
          {{ state.summonerLevel }}
        </span>
      </div>

      <div class="min-w-0 pb-1">
        <h1 class="truncate text-4xl font-semibold tracking-tight text-highlighted">
          {{ name ?? 'Logging in…' }}<span v-if="tag" class="ml-1 text-2xl font-medium text-dimmed">#{{ tag }}</span>
        </h1>
      </div>
    </section>
  </div>
</template>
