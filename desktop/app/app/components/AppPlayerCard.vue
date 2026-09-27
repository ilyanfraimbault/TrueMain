<script setup lang="ts">
import type { GameflowPhase } from '~/types/lcu'

/**
 * The player, at the foot of the sidebar: who the client is logged in as, and
 * where it is in the gameflow. With no client it says it is waiting for one —
 * the rest of the app works without it.
 */
const { state } = useLcuState()
const { profileIconOf } = useChampionStatics()

const name = computed(() => state.value.riotId?.split('#')[0] ?? null)
const tag = computed(() => state.value.riotId?.split('#')[1] ?? null)
const icon = computed(() => (state.value.profileIconId !== null ? profileIconOf(state.value.profileIconId) : null))

/** The client's phase in the player's words. `live` phases pulse. */
const STATUS: Partial<Record<GameflowPhase, { label: string, live?: boolean }>> = {
  Lobby: { label: 'In lobby' },
  Matchmaking: { label: 'In queue', live: true },
  ReadyCheck: { label: 'Match found', live: true },
  ChampSelect: { label: 'Champion select', live: true },
  InProgress: { label: 'In game', live: true },
  Reconnect: { label: 'Reconnecting', live: true },
  WaitingForStats: { label: 'Post-game' },
  PreEndOfGame: { label: 'Post-game' },
  EndOfGame: { label: 'Post-game' },
}
const status = computed(() => {
  if (!state.value.connected) return { label: 'Client not running', live: false }
  return STATUS[state.value.phase] ?? { label: 'Online' }
})
</script>

<template>
  <div class="surface flex items-center gap-3 rounded-xl p-2.5">
    <div class="relative shrink-0">
      <div class="size-10 overflow-hidden rounded-lg bg-ink-800 ring-1 ring-default">
        <img v-if="icon && state.connected" :src="icon" alt="" class="size-full object-cover">
        <div v-else class="flex size-full items-center justify-center">
          <UIcon name="i-lucide-user-round" class="size-5 text-dimmed" />
        </div>
      </div>
      <span
        v-if="state.connected && state.summonerLevel !== null"
        class="absolute -bottom-1.5 left-1/2 -translate-x-1/2 rounded-full border border-default bg-ink-950 px-1.5 text-[9px] font-semibold leading-4 tabular-nums text-highlighted"
      >{{ state.summonerLevel }}</span>
    </div>

    <div class="min-w-0 flex-1 leading-tight">
      <p class="truncate text-sm font-semibold text-highlighted">
        {{ state.connected ? (name ?? 'Logging in…') : 'League client' }}<span v-if="tag && state.connected" class="font-normal text-dimmed"> #{{ tag }}</span>
      </p>
      <p class="mt-0.5 flex items-center gap-1.5 truncate text-[11px] text-muted">
        <span class="relative flex size-1.5 shrink-0">
          <span v-if="status.live || !state.connected" class="absolute inline-flex size-full animate-ping rounded-full opacity-60" :class="state.connected ? 'bg-primary' : 'bg-ink-400'" />
          <span class="relative inline-flex size-1.5 rounded-full" :class="!state.connected ? 'bg-ink-400' : status.live ? 'bg-primary' : 'bg-success'" />
        </span>
        {{ status.label }}
      </p>
    </div>
  </div>
</template>
