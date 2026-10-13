<script setup lang="ts">
import type { GameflowPhase } from '~/types/lcu'
import type { AccountIdentity } from '#common/components/Account.vue'
import { riotIdLabel } from '#shared/utils/truemain-path'

/**
 * The player, at the foot of the sidebar: who the client is logged in as, and
 * where it is in the gameflow. With no client it says it is waiting for one —
 * the rest of the app works without it. Drawn by the site's `Account` (#2027),
 * the level on the picture's edge and the client's status under the name.
 *
 * `compact`, in the sidebar's rail: the picture and the status dot alone, the
 * words in a tooltip.
 */
defineProps<{ compact?: boolean }>()

const { state } = useLcuState()
const { patch } = useChampionStatics()

const identity = computed<AccountIdentity>(() => {
  if (!state.value.connected) return { gameName: 'League client', tagLine: null }
  const [gameName, tagLine] = state.value.riotId?.split('#') ?? []
  return { gameName: gameName ?? 'Logging in…', tagLine: tagLine ?? null, profileIconId: state.value.profileIconId }
})

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
const dot = computed(() => (!state.value.connected ? 'bg-ink-400' : status.value.live ? 'bg-primary' : 'bg-success'))
const title = computed(() => riotIdLabel(identity.value.gameName, identity.value.tagLine))
</script>

<template>
  <UTooltip v-if="compact" :text="`${title} · ${status.label}`">
    <Account :identity="identity" :patch="patch || null" size="lg" :to="false" :ui="{ body: 'sr-only' }">
      <template #avatar-badge>
        <span class="absolute -bottom-0.5 -right-0.5 flex size-2.5 rounded-full border-2 border-default" :class="dot" />
      </template>
    </Account>
  </UTooltip>
  <div v-else class="surface flex items-center rounded-xl p-2.5">
    <Account :identity="identity" :patch="patch || null" size="lg" :to="false" class="flex-1">
      <template v-if="state.connected && state.summonerLevel !== null" #avatar-badge>
        <span class="absolute -bottom-1.5 left-1/2 -translate-x-1/2 rounded-full border border-default bg-ink-950 px-1.5 text-[9px] font-semibold leading-4 tabular-nums text-highlighted">{{ state.summonerLevel }}</span>
      </template>
      <template #subline>
        <span class="flex min-w-0 items-center gap-1.5 truncate text-[11px] text-muted">
          <span class="relative flex size-1.5 shrink-0">
            <span v-if="status.live || !state.connected" class="absolute inline-flex size-full animate-ping rounded-full opacity-60" :class="state.connected ? 'bg-primary' : 'bg-ink-400'" />
            <span class="relative inline-flex size-1.5 rounded-full" :class="dot" />
          </span>
          {{ status.label }}
        </span>
      </template>
    </Account>
  </div>
</template>
