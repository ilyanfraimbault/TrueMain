<script setup lang="ts">
import type { GameState, GameTeam, NeutralTimers } from '~/types/game'
import type { ObjectiveKey, ObjectiveStatus } from '~/utils/objective-timers'
import { leftTeam } from '~/utils/item-value'
import { SOON_SECONDS, drakeStandings, formatCountdown, objectiveStatus } from '~/utils/objective-timers'

/**
 * The neutral objectives' next spawns (#2078): a pill each for the dragon
 * pit, the Voidgrubs, the Rift Herald and Baron — a countdown, "Up" while it
 * stands, "Taken" (in the colour of the side that took it) once it will not
 * come again. Beside the dragon, the drakes each side holds and the soul
 * point; the pit reads "Elder" once a side holds the soul. Beside the grubs,
 * the grubs each side slew.
 *
 * Only what the game shows every player: the shell derives the times from
 * the season's spawn rules and the kills the event feed announces
 * (`crates/live-client/src/timers.rs`). Ours on the left in blue, theirs in
 * red (blue and red sides when spectating), as in the other panels.
 */
const props = defineProps<{ game: GameState, syncedAt: number }>()

// The shell sends game times; the clock runs here between two readings.
const now = ref(Date.now())
let ticking: ReturnType<typeof setInterval> | undefined
onMounted(() => {
  ticking = setInterval(() => (now.value = Date.now()), 1000)
})
onBeforeUnmount(() => clearInterval(ticking))
const clock = computed(() => props.game.gameTime + Math.max(0, now.value - props.syncedAt) / 1000)

const left = computed(() => leftTeam(props.game))
const side = (team: GameTeam | null) => (team === null ? 'text-muted' : team === left.value ? 'text-ally' : 'text-enemy')

const ICONS: Record<ObjectiveKey, string> = {
  dragon: 'i-lucide-flame',
  grubs: 'i-lucide-bug',
  herald: 'i-lucide-eye',
  baron: 'i-lucide-crown',
}

const timers = computed<NeutralTimers | null>(() => props.game.objectives.timers)
const drakes = computed(() => drakeStandings(props.game.objectives, left.value))
const grubs = computed(() => {
  const { order, chaos } = props.game.objectives
  const ours = left.value === 'ORDER' ? order.grubs : chaos.grubs
  const theirs = left.value === 'ORDER' ? chaos.grubs : order.grubs
  return ours + theirs > 0 ? { ours, theirs } : null
})

const rows = computed(() => {
  const all = timers.value
  if (!all) return []
  const row = (key: ObjectiveKey, label: string) => ({ key, label, icon: ICONS[key], status: objectiveStatus(all[key], clock.value) })
  return [
    row('dragon', all.elder ? 'Elder' : 'Drake'),
    row('grubs', 'Voidgrubs'),
    row('herald', 'Herald'),
    row('baron', 'Baron'),
  ]
})

function value(status: ObjectiveStatus) {
  switch (status.kind) {
    case 'countdown': return { text: formatCountdown(status.seconds), tone: status.seconds <= SOON_SECONDS ? 'text-highlighted' : 'text-muted' }
    case 'up': return { text: 'Up', tone: 'text-primary font-semibold' }
    case 'done': return { text: status.by ? 'Taken' : 'Gone', tone: status.by ? side(status.by) : 'text-dimmed' }
  }
}
</script>

<template>
  <div class="flex flex-col gap-1.5">
    <p class="flex items-center gap-1 text-[10px] font-semibold uppercase tracking-wider text-dimmed">
      <AppMark class="size-2.5" />
      Objectives
    </p>
    <ul v-if="rows.length" class="flex flex-col gap-1">
      <li
        v-for="row in rows"
        :key="row.key"
        :data-objective="row.key"
        :data-status="row.status.kind"
        class="grid grid-cols-[0.875rem_auto_1fr_2.25rem] items-center gap-1.5 rounded bg-elevated/60 px-1.5 py-0.5"
      >
        <UIcon :name="row.icon" class="size-3.5 text-dimmed" />
        <span class="whitespace-nowrap text-[11px] text-default">{{ row.label }}</span>
        <span v-if="row.key === 'dragon' && drakes.ours.count + drakes.theirs.count > 0" class="flex items-center justify-self-end gap-1 text-[11px] tabular-nums" data-drakes>
          <span class="text-ally" :class="{ 'font-semibold': drakes.ours.soulPoint || drakes.ours.soul }">{{ drakes.ours.count }}</span>
          <span class="text-dimmed">–</span>
          <span class="text-enemy" :class="{ 'font-semibold': drakes.theirs.soulPoint || drakes.theirs.soul }">{{ drakes.theirs.count }}</span>
          <span v-if="drakes.ours.soul || drakes.theirs.soul" class="whitespace-nowrap rounded px-1 text-[9px] font-semibold uppercase" :class="drakes.ours.soul ? 'bg-ally/20 text-ally' : 'bg-enemy/20 text-enemy'">Soul</span>
          <span v-else-if="drakes.ours.soulPoint || drakes.theirs.soulPoint" class="whitespace-nowrap rounded px-1 text-[9px] font-semibold uppercase" :class="drakes.ours.soulPoint && !drakes.theirs.soulPoint ? 'bg-ally/20 text-ally' : drakes.theirs.soulPoint && !drakes.ours.soulPoint ? 'bg-enemy/20 text-enemy' : 'bg-elevated text-highlighted'">Soul pt</span>
        </span>
        <span v-else-if="row.key === 'grubs' && grubs" class="flex items-center justify-self-end gap-1 text-[11px] tabular-nums" data-grubs>
          <span class="text-ally">{{ grubs.ours }}</span>
          <span class="text-dimmed">–</span>
          <span class="text-enemy">{{ grubs.theirs }}</span>
        </span>
        <span v-else />
        <span class="justify-self-end text-[11px] tabular-nums" :class="value(row.status).tone">{{ value(row.status).text }}</span>
      </li>
    </ul>
    <p v-else class="text-[11px] leading-snug text-muted">
      Timers follow Summoner's Rift's standard rules, not this mode's.
    </p>
  </div>
</template>
