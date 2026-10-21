<script setup lang="ts">
import type { GameState, NeutralTimers } from '~/types/game'
import type { ObjectiveKey, PillStatus } from '~/utils/objective-timers'
import { SOON_SECONDS, dragonLabel, drakeIcon, formatCountdown, objectiveIcon, pillStatus } from '~/utils/objective-timers'

/**
 * The neutral objectives' next spawns as one compact strip (#2078, redrawn by
 * #2184), icons only. A pill each for the dragon pit, the Voidgrubs, the Rift
 * Herald and Baron, side by side: the game's own minimap icon and, until it
 * spawns, an `m:ss` countdown — rose-gold within `SOON_SECONDS` — or, once it
 * stands, the icon lit with a green dot. A pill that does not matter yet (the
 * Herald before the grubs are gone, Baron before the Herald is) stays, in grey,
 * so the strip keeps the player's landmarks; a camp that will not come again
 * leaves it. Every pill is as wide as the widest countdown, so a ticking
 * countdown never moves the strip.
 *
 * Under it, the soul race: blue side's drakes on the left, red side's on the
 * right, ringed in their side's colour, meeting at the pit's next drake —
 * its element once the Rift turned to it, the Elder once a side holds the
 * soul. The line is there from the start, so the strip is as tall at 0:00 as
 * at the soul: nothing in a game changes its height.
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
/** The pit's next drake shows between the sides once there is a race: a drake fell. */
const racing = computed(() => blue.value.length + red.value.length > 0)

const NAMES: Record<Exclude<ObjectiveKey, 'dragon'>, string> = { grubs: 'Voidgrubs', herald: 'Rift Herald', baron: 'Baron Nashor' }

const pills = computed(() => {
  const all = timers.value
  if (!all) return []
  return (['dragon', 'grubs', 'herald', 'baron'] as const)
    .map(key => ({
      key,
      name: key === 'dragon' ? dragonLabel(all) : NAMES[key],
      icon: objectiveIcon(key, all),
      status: pillStatus(key, all, clock.value),
    }))
    .filter(pill => pill.status.kind !== 'done')
})

const PILL: Record<PillStatus['kind'], string> = {
  countdown: 'bg-elevated/70 ring-default',
  up: 'bg-primary/10 ring-primary/40',
  waiting: 'bg-elevated/40 ring-transparent',
  done: '',
}
const soon = (status: PillStatus) => status.kind === 'countdown' && status.seconds <= SOON_SECONDS
</script>

<template>
  <div v-if="timers" class="flex flex-col gap-1">
    <ol class="flex h-6 items-center justify-center gap-1">
      <li
        v-for="pill in pills"
        :key="pill.key"
        :data-objective="pill.key"
        :data-status="pill.status.kind"
        class="flex h-6 w-16 items-center justify-center gap-1 rounded-full ring-1 ring-inset"
        :class="PILL[pill.status.kind]"
      >
        <img
          :src="pill.icon"
          :alt="pill.name"
          class="size-4 shrink-0"
          :class="{
            'opacity-45 grayscale': pill.status.kind === 'waiting',
            'drop-shadow-[0_0_4px_var(--ui-primary)]': pill.status.kind === 'up',
          }"
          data-icon
        >
        <span
          v-if="pill.status.kind === 'countdown' || pill.status.kind === 'waiting'"
          class="text-[11px] leading-none tabular-nums"
          :class="pill.status.kind === 'waiting' ? 'text-dimmed' : soon(pill.status) ? 'font-semibold text-primary' : 'text-muted'"
          data-countdown
        >
          {{ formatCountdown(pill.status.seconds) }}
        </span>
        <span v-else class="size-1.5 rounded-full bg-success shadow-[0_0_4px] shadow-success" data-up role="img" aria-label="Up" />
      </li>
    </ol>
    <!-- The soul race: blue side's drakes from the left, red side's from the right, the pit's next between them. -->
    <div class="grid h-3.5 grid-cols-[1fr_auto_1fr] items-center gap-1" data-soul-race>
      <span class="flex items-center justify-end gap-0.5" data-side="blue">
        <span class="h-px flex-1 bg-linear-to-r from-transparent to-ally/50" />
        <img v-for="(element, index) in blue" :key="index" :src="drakeIcon(element)" :alt="element" class="size-3.5 rounded-full ring-1 ring-ally/70">
      </span>
      <span class="flex size-3.5 items-center justify-center">
        <img v-if="racing" :src="objectiveIcon('dragon', timers)" :alt="dragonLabel(timers)" class="size-3.5" data-next>
        <span v-else class="size-1 rounded-full bg-accented" />
      </span>
      <span class="flex items-center gap-0.5" data-side="red">
        <img v-for="(element, index) in red" :key="index" :src="drakeIcon(element)" :alt="element" class="size-3.5 rounded-full ring-1 ring-enemy/70">
        <span class="h-px flex-1 bg-linear-to-r from-enemy/50 to-transparent" />
      </span>
    </div>
  </div>
  <p v-else class="flex items-center gap-1.5 text-[11px] leading-snug text-muted">
    <AppMark class="size-3 shrink-0" />
    Timers follow Summoner's Rift's standard rules, not this mode's.
  </p>
</template>
