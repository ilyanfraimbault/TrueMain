<script setup lang="ts">
import type { GamePlayer, GameState, GameTeam } from '~/types/game'
import { LANES, LANE_LABELS, laneIconUrl } from '~/types/draft'

/**
 * The running game, read through its own API (#1748): the ten players, lane
 * by lane, ours on the left and theirs mirrored on the right, each with their
 * level, summoner spells, K/D/A and items as they buy them. The strip over it
 * carries the map, the kills of each side and the game clock.
 *
 * This is the frame the in-game panels of #1747 land in: over the board, the
 * next item to complete (#1751) when we are a player rather than spectating.
 */
const props = defineProps<{
  game: GameState
  /** When `game.gameTime` was read, on this page's clock. */
  syncedAt: number
}>()

// ─── The clock ──────────────────────────────────────────────────────────────

/**
 * The game is read every couple of seconds and only a change is sent, so the
 * time it carries can be minutes old. The clock runs from it on the page's own
 * time, and every update or snapshot sets it right again.
 */
const now = ref(Date.now())
let ticking: ReturnType<typeof setInterval> | undefined
onMounted(() => {
  ticking = setInterval(() => (now.value = Date.now()), 1000)
})
onBeforeUnmount(() => clearInterval(ticking))

const clock = computed(() => props.game.gameTime + Math.max(0, now.value - props.syncedAt) / 1000)

function minutes(seconds: number) {
  const whole = Math.floor(seconds)
  return `${Math.floor(whole / 60)}:${String(whole % 60).padStart(2, '0')}`
}

const MAPS: Record<number, string> = { 11: 'Summoner\'s Rift', 12: 'Howling Abyss', 30: 'Arena' }
const mapName = computed(() => MAPS[props.game.mapNumber] ?? 'Live game')

// ─── The board ──────────────────────────────────────────────────────────────

/** Ours on the left. Spectating, blue side is. */
const left = computed<GameTeam>(() => props.game.myTeam ?? 'ORDER')
const right = computed<GameTeam>(() => (left.value === 'ORDER' ? 'CHAOS' : 'ORDER'))

const laneIndex = (position: string) => {
  const index = (LANES as readonly string[]).indexOf(position)
  return index === -1 ? LANES.length : index
}

/** A side in lane order; a player on no lane keeps the game's order after the five. */
const side = (team: GameTeam) => props.game.players
  .filter(player => player.team === team)
  .map((player, order) => ({ player, order }))
  .sort((a, b) => laneIndex(a.player.position) - laneIndex(b.player.position) || a.order - b.order)
  .map(({ player }) => player)

/** Lane by lane, so each row is a lane's face-off. */
const rows = computed(() => {
  const ours = side(left.value)
  const theirs = side(right.value)
  return Array.from({ length: Math.max(ours.length, theirs.length) }, (_, index) => {
    const ally = ours[index] ?? null
    const enemy = theirs[index] ?? null
    const lane = [ally?.position, enemy?.position].find(position => position && position in LANE_LABELS) ?? null
    return { ally, enemy, lane: lane as keyof typeof LANE_LABELS | null, key: `${ally?.riotId}-${enemy?.riotId}` }
  })
})

const kills = (team: GameTeam) => props.game.players.filter(player => player.team === team).reduce((sum, player: GamePlayer) => sum + player.kills, 0)

const sideLabel = (team: GameTeam, ours: boolean) => {
  if (props.game.myTeam) return ours ? 'Your team' : 'Enemy team'
  return team === 'ORDER' ? 'Blue side' : 'Red side'
}
</script>

<template>
  <div class="flex h-full flex-col gap-3 overflow-y-auto px-4 pb-4 pt-3">
    <header class="grid grid-cols-[1fr_auto_1fr] items-center gap-4">
      <div class="flex items-center gap-2.5">
        <span class="relative flex size-2.5 items-center justify-center">
          <span class="absolute inline-flex size-full animate-tm-pulse rounded-full bg-primary" />
          <span class="relative inline-flex size-1.5 rounded-full bg-primary" />
        </span>
        <span class="stat-label">{{ mapName }}</span>
      </div>

      <div class="flex items-baseline gap-3" :title="`Kills: ${sideLabel(left, true).toLowerCase()} ${kills(left)}, ${sideLabel(right, false).toLowerCase()} ${kills(right)}`">
        <span class="stat-value text-2xl leading-none text-ally">{{ kills(left) }}</span>
        <UIcon name="i-lucide-swords" class="size-4 self-center text-dimmed" />
        <span class="stat-value text-2xl leading-none text-enemy">{{ kills(right) }}</span>
      </div>

      <div class="flex items-center gap-3 justify-self-end">
        <span class="stat-value text-xl leading-none tabular-nums" title="Game time">{{ minutes(clock) }}</span>
        <slot name="actions" />
      </div>
    </header>

    <GameNextItem v-if="game.myTeam" :game="game" />

    <section class="surface overflow-hidden rounded-xl">
      <div class="grid grid-cols-[minmax(0,1fr)_2.5rem_minmax(0,1fr)] items-center gap-3 border-b border-default px-4 py-2">
        <div class="flex items-center gap-2">
          <span class="h-3 w-0.5 rounded-full bg-ally/70" />
          <span class="stat-label">{{ sideLabel(left, true) }}</span>
        </div>
        <span />
        <div class="flex items-center justify-end gap-2">
          <span class="stat-label">{{ sideLabel(right, false) }}</span>
          <span class="h-3 w-0.5 rounded-full bg-enemy/70" />
        </div>
      </div>

      <ul class="divide-y divide-default">
        <li
          v-for="row in rows"
          :key="row.key"
          class="grid min-h-[5.25rem] grid-cols-[minmax(0,1fr)_2.5rem_minmax(0,1fr)] items-center gap-3 px-4 py-2.5"
        >
          <GamePlayerSide
            :player="row.ally"
            :clock="clock"
            class="-mx-2 rounded-lg px-2 py-1"
            :class="row.ally?.isMe && 'bg-primary/8'"
          />
          <div class="flex justify-center">
            <img v-if="row.lane" :src="laneIconUrl(row.lane)" :alt="LANE_LABELS[row.lane]" :title="LANE_LABELS[row.lane]" class="size-5 opacity-70">
          </div>
          <GamePlayerSide
            :player="row.enemy"
            :clock="clock"
            mirrored
            class="-mx-2 rounded-lg px-2 py-1"
            :class="row.enemy?.isMe && 'bg-primary/8'"
          />
        </li>
      </ul>
    </section>
  </div>
</template>
