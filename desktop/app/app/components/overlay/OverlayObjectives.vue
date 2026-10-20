<script setup lang="ts">
import type { GameState, NeutralTimers } from '~/types/game'
import type { ObjectiveKey } from '~/utils/objective-timers'
import { SOON_SECONDS, dragonLabel, drakeIcon, formatCountdown, objectiveIcon, objectiveStatus } from '~/utils/objective-timers'

/**
 * The neutral objectives' next spawns (#2078), icons only: a row each for the
 * dragon pit, the Voidgrubs, the Rift Herald and Baron, with the game's own
 * minimap icon and, until it spawns, a countdown — the icon alone once it
 * stands. A camp that will not come again (taken, or gone from the map) leaves
 * the panel. The dragon pit's icon is its next drake's element once the Rift
 * turned to it (after the second drake — the API does not name the first two),
 * the Elder's once a side holds the soul; flanking it, the drakes each side
 * took, blue side's on the left, red side's on the right, as the scoreboard
 * draws them.
 *
 * Only what the game shows every player: the shell derives the times from
 * the season's spawn rules and the kills the event feed announces
 * (`crates/live-client/src/timers.rs`).
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

const timers = computed<NeutralTimers | null>(() => props.game.objectives.timers)
const blue = computed(() => props.game.objectives.order.drakes)
const red = computed(() => props.game.objectives.chaos.drakes)

const NAMES: Record<Exclude<ObjectiveKey, 'dragon'>, string> = { grubs: 'Voidgrubs', herald: 'Rift Herald', baron: 'Baron Nashor' }

const rows = computed(() => {
  const all = timers.value
  if (!all) return []
  return (['dragon', 'grubs', 'herald', 'baron'] as const)
    .map(key => ({
      key,
      name: key === 'dragon' ? dragonLabel(all) : NAMES[key],
      icon: objectiveIcon(key, all),
      status: objectiveStatus(all[key], clock.value),
    }))
    .filter(row => row.status.kind !== 'done')
})
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
        class="items-center gap-1.5 rounded bg-elevated/60 px-1.5 py-0.5"
        :class="row.key === 'dragon' ? 'grid grid-cols-[1fr_auto_1fr]' : 'flex justify-between'"
      >
        <!-- The dragon pit sits between the drakes each side took; the other camps are their icon and countdown, apart. -->
        <template v-if="row.key === 'dragon'">
          <span class="flex items-center gap-0.5" data-side="blue">
            <img v-for="(element, index) in blue" :key="index" :src="drakeIcon(element)" :alt="element" class="size-3.5 rounded-full ring-1 ring-ally/70">
          </span>
          <!-- The countdown's slot is kept while the drake stands, so the pit's icon does not move when it spawns. -->
          <span class="grid grid-cols-[1rem_2.25rem] items-center gap-1">
            <img :src="row.icon" :alt="row.name" :title="row.name" class="size-4" data-icon>
            <span v-if="row.status.kind === 'countdown'" class="text-[11px] tabular-nums" :class="row.status.seconds <= SOON_SECONDS ? 'text-highlighted' : 'text-muted'" data-countdown>
              {{ formatCountdown(row.status.seconds) }}
            </span>
          </span>
          <span class="flex items-center justify-end gap-0.5" data-side="red">
            <img v-for="(element, index) in red" :key="index" :src="drakeIcon(element)" :alt="element" class="size-3.5 rounded-full ring-1 ring-enemy/70">
          </span>
        </template>
        <template v-else>
          <img :src="row.icon" :alt="row.name" :title="row.name" class="size-4" data-icon>
          <span v-if="row.status.kind === 'countdown'" class="text-[11px] tabular-nums" :class="row.status.seconds <= SOON_SECONDS ? 'text-highlighted' : 'text-muted'" data-countdown>
            {{ formatCountdown(row.status.seconds) }}
          </span>
        </template>
      </li>
    </ul>
    <p v-else class="text-[11px] leading-snug text-muted">
      Timers follow Summoner's Rift's standard rules, not this mode's.
    </p>
  </div>
</template>
