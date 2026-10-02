<script setup lang="ts">
import type { GameTeam } from '~/types/game'
import type { LoadingPlayer } from '~/types/loading'
import { LANES } from '~/types/draft'

/**
 * The loading screen (#1753): each player's games on the champion they are
 * on among their last twenty and their win rate on it, and their latest games
 * as bars — ours on the left, theirs on the right, lane against lane. Read
 * through the player's own client from the loading screen on, never before.
 * Counts only: no score made of them.
 */
const props = defineProps<{ players: LoadingPlayer[] }>()

const { portraitOf, nameOf } = useChampionStatics()

const mine = computed<GameTeam>(() => props.players.find(player => player.isMe)?.team ?? 'ORDER')
const laneIndex = (position: string) => {
  const index = (LANES as readonly string[]).indexOf(position)
  return index === -1 ? LANES.length : index
}
const side = (team: GameTeam) => props.players
  .filter(player => player.team === team)
  .sort((a, b) => laneIndex(a.position) - laneIndex(b.position))
const columns = computed(() => [side(mine.value), side(mine.value === 'ORDER' ? 'CHAOS' : 'ORDER')])

const name = (riotId: string) => riotId.split('#')[0]
const rate = (player: LoadingPlayer) => {
  const form = player.form!
  return Math.round((form.championWins / form.championGames) * 100)
}
</script>

<template>
  <div class="grid grid-cols-2 gap-x-4">
    <ul v-for="(column, index) in columns" :key="index" class="flex flex-col gap-1">
      <li
        v-for="player in column"
        :key="player.riotId"
        class="flex items-center gap-2 rounded-md px-1 py-0.5"
      >
        <img
          v-if="portraitOf(player.championId)"
          :src="portraitOf(player.championId)!"
          :alt="nameOf(player.championId)"
          class="size-7 shrink-0 rounded ring-1"
          :class="index === 0 ? 'ring-ally/50' : 'ring-enemy/50'"
        >
        <span v-else class="size-7 shrink-0 rounded bg-elevated" />
        <div class="min-w-0 flex-1 leading-tight">
          <p class="truncate text-xs font-medium text-highlighted">{{ name(player.riotId) }}</p>
          <p v-if="player.form && player.form.championGames > 0" class="text-[11px] tabular-nums text-muted">
            {{ player.form.championGames }} · <span :class="rate(player) >= 50 ? 'text-data-good' : 'text-data-bad'">{{ rate(player) }}%</span>
          </p>
          <p v-else-if="player.form" class="text-[11px] text-dimmed">1st</p>
          <p v-else-if="player.failed" class="text-[11px] text-dimmed">–</p>
          <USkeleton v-else class="mt-1 h-2 w-12" />
        </div>
        <LoadingRecent v-if="player.form?.recent.length" :games="player.form.recent" />
      </li>
    </ul>
  </div>
</template>
