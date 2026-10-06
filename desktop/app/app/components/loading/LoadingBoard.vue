<script setup lang="ts">
import type { GameTeam } from '~/types/game'
import type { LoadingPlayer } from '~/types/loading'
import { LANES } from '~/types/draft'
import type { LaneEdge } from '~/utils/lane-edge'

/**
 * The loading screen (#1753): each player's games on the champion they are
 * on among their last twenty and their win rate on it, and their latest games
 * as bars — ours on the left, theirs on the right, lane against lane, and
 * between them, when given, the side each lane favours (#1863). Read
 * through the player's own client from the loading screen on, never before.
 * Counts only: no score made of them. A player who hides their name shows
 * as their champion, anonymous, with nothing read about them. A true main of
 * the champion they are on carries the TrueMain mark after their name (#1910).
 */
const props = defineProps<{
  players: LoadingPlayer[]
  /** Each lane's edge, by position (#1863): drawn between the two columns when given. */
  edges?: Map<string, LaneEdge>
}>()

const { portraitOf, nameOf } = useChampionStatics()
/** The true-main mark (#1910), from the lookup the page asked. */
const markOf = useTruemainMarkOf()

const mine = computed<GameTeam>(() => props.players.find(player => player.isMe)?.team ?? 'ORDER')
const laneIndex = (position: string) => {
  const index = (LANES as readonly string[]).indexOf(position)
  return index === -1 ? LANES.length : index
}
const side = (team: GameTeam) => props.players
  .filter(player => player.team === team)
  .sort((a, b) => laneIndex(a.position) - laneIndex(b.position))
const columns = computed(() => [side(mine.value), side(mine.value === 'ORDER' ? 'CHAOS' : 'ORDER')])
/** Lane by lane: ours, theirs and the edge between, a row each. */
const rows = computed(() => {
  const [ours, theirs] = columns.value
  return Array.from({ length: Math.max(ours!.length, theirs!.length) }, (_, index) => {
    const ally = ours![index] ?? null
    const enemy = theirs![index] ?? null
    const position = ally?.position ?? enemy?.position ?? ''
    const sameLane = ally && enemy && ally.position === enemy.position
    return { ally, enemy, edge: sameLane ? props.edges?.get(position) : undefined, key: `${index}-${position}` }
  })
})

const name = (riotId: string) => riotId.split('#')[0]
const rate = (player: LoadingPlayer) => {
  const form = player.form!
  return Math.round((form.championWins / form.championGames) * 100)
}
</script>

<template>
  <ul class="flex flex-col gap-1">
    <li
      v-for="row in rows"
      :key="row.key"
      class="grid items-center gap-x-2"
      :class="edges ? 'grid-cols-[minmax(0,1fr)_2.25rem_minmax(0,1fr)]' : 'grid-cols-2 gap-x-4'"
    >
      <template v-for="(player, index) in [row.ally, row.enemy]" :key="index">
        <div
          v-if="player"
          class="flex min-w-0 items-center gap-2 rounded-md px-1 py-0.5"
          :class="index === 0 ? 'order-1' : 'order-3'"
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
            <template v-if="player.anonymous">
              <p class="flex items-center gap-1 truncate text-xs font-medium text-muted">
                <UIcon name="i-lucide-eye-off" class="size-3 shrink-0" />{{ nameOf(player.championId) }}
              </p>
              <p class="text-[11px] text-dimmed">Anonymous</p>
            </template>
            <template v-else>
              <p class="flex min-w-0 items-center gap-1 text-xs font-medium text-highlighted">
                <span class="truncate">{{ name(player.riotId) || nameOf(player.championId) }}</span>
                <TruemainMark v-if="markOf(player)" :mark="markOf(player)!" />
              </p>
              <p v-if="player.form && player.form.championGames > 0" class="text-[11px] tabular-nums text-muted">
                {{ player.form.championGames }} · <span :class="rate(player) >= 50 ? 'text-data-good' : 'text-data-bad'">{{ rate(player) }}%</span>
              </p>
              <p v-else-if="player.form" class="text-[11px] text-dimmed">1st</p>
              <p v-else-if="player.failed" class="text-[11px] text-dimmed">–</p>
              <USkeleton v-else class="mt-1 h-2 w-12" />
            </template>
          </div>
          <LoadingRecent v-if="player.form?.recent.length" :games="player.form.recent" />
        </div>
        <span v-else :class="index === 0 ? 'order-1' : 'order-3'" />
      </template>
      <div v-if="edges" class="order-2 flex justify-center">
        <GameLaneEdge :edge="row.edge" />
      </div>
    </li>
  </ul>
</template>
