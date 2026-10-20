<script setup lang="ts">
import type { GameState, NeutralTimers } from '~/types/game'
import type { ObjectiveKey } from '~/utils/objective-timers'
import { leftTeam } from '~/utils/item-value'
import { SOON_SECONDS, dragonLabel, drakeIcon, drakeStandings, formatCountdown, objectiveIcon, objectiveStatus } from '~/utils/objective-timers'

/**
 * The neutral objectives' next spawns (#2078): a row each for the dragon pit,
 * the Voidgrubs, the Rift Herald and Baron, with the game's own minimap icon —
 * a countdown, or a green dot while it stands. A camp that will not come again
 * (taken, or gone from the map) leaves the panel. The dragon pit names its
 * next drake's element once the Rift turned to it (after the second drake —
 * the API does not name the first two), the Elder once a side holds the soul;
 * under it, the drakes each side took, as their icons — ours on the left
 * ringed in blue, theirs on the right in red, as the scoreboard draws them —
 * and between them the soul point or the soul.
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
const timers = computed<NeutralTimers | null>(() => props.game.objectives.timers)
const drakes = computed(() => drakeStandings(props.game.objectives, left.value))
const anyDrake = computed(() => drakes.value.ours.elements.length + drakes.value.theirs.elements.length > 0)

const NAMES: Record<Exclude<ObjectiveKey, 'dragon'>, string> = { grubs: 'Voidgrubs', herald: 'Herald', baron: 'Baron' }

const rows = computed(() => {
  const all = timers.value
  if (!all) return []
  return (['dragon', 'grubs', 'herald', 'baron'] as const)
    .map(key => ({
      key,
      label: key === 'dragon' ? dragonLabel(all) : NAMES[key],
      icon: objectiveIcon(key, all),
      status: objectiveStatus(all[key], clock.value),
    }))
    .filter(row => row.status.kind !== 'done')
})

const soulBadge = computed(() => {
  const { ours, theirs } = drakes.value
  if (ours.soul || theirs.soul) return { text: 'Soul', tone: ours.soul ? 'bg-ally/20 text-ally' : 'bg-enemy/20 text-enemy' }
  if (ours.soulPoint && theirs.soulPoint) return { text: 'Soul point', tone: 'bg-elevated text-highlighted' }
  if (ours.soulPoint || theirs.soulPoint) return { text: 'Soul point', tone: ours.soulPoint ? 'bg-ally/20 text-ally' : 'bg-enemy/20 text-enemy' }
  return null
})
</script>

<template>
  <div class="flex flex-col gap-1.5">
    <p class="flex items-center gap-1 text-[10px] font-semibold uppercase tracking-wider text-dimmed">
      <AppMark class="size-2.5" />
      Objectives
    </p>
    <ul v-if="rows.length" class="flex flex-col gap-1">
      <template v-for="row in rows" :key="row.key">
        <li
          :data-objective="row.key"
          :data-status="row.status.kind"
          class="grid grid-cols-[1rem_1fr_2.25rem] items-center gap-1.5 rounded bg-elevated/60 px-1.5 py-0.5"
        >
          <img :src="row.icon" alt="" class="size-4 shrink-0" data-icon>
          <span class="whitespace-nowrap text-[11px] text-default">{{ row.label }}</span>
          <span v-if="row.status.kind === 'countdown'" class="justify-self-end text-[11px] tabular-nums" :class="row.status.seconds <= SOON_SECONDS ? 'text-highlighted' : 'text-muted'">
            {{ formatCountdown(row.status.seconds) }}
          </span>
          <span v-else class="mr-1 size-1.5 justify-self-end rounded-full bg-success shadow-[0_0_4px] shadow-success" data-up role="img" aria-label="Up" />
        </li>
        <li v-if="row.key === 'dragon' && anyDrake" class="grid grid-cols-[1fr_auto_1fr] items-center gap-1.5 px-1.5" data-drakes>
          <span class="flex items-center gap-0.5" data-side="ours">
            <img v-for="(element, index) in drakes.ours.elements" :key="index" :src="drakeIcon(element)" :alt="element" class="size-3.5 rounded-full ring-1 ring-ally/70">
          </span>
          <span v-if="soulBadge" class="whitespace-nowrap rounded px-1 text-[9px] font-semibold uppercase" :class="soulBadge.tone" data-soul>{{ soulBadge.text }}</span>
          <span v-else />
          <span class="flex items-center justify-end gap-0.5" data-side="theirs">
            <img v-for="(element, index) in drakes.theirs.elements" :key="index" :src="drakeIcon(element)" :alt="element" class="size-3.5 rounded-full ring-1 ring-enemy/70">
          </span>
        </li>
      </template>
    </ul>
    <p v-else class="text-[11px] leading-snug text-muted">
      Timers follow Summoner's Rift's standard rules, not this mode's.
    </p>
  </div>
</template>
