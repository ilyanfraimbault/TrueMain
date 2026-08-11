<script setup lang="ts">
import type { ChampionStaticListItem } from '~~/shared/types/static-data'
import type { ChampionMatchupEntry } from '~~/shared/types/champions'
import { formatPercentage } from '~~/shared/utils/ddragon'
import { formatGoldDiff } from '~/utils/lane-verdict'
import { winRateTone } from '~/utils/rate-tone'

const props = defineProps<{
  entry: ChampionMatchupEntry
  opponent: ChampionStaticListItem | null
  /**
   * Where the row leads — the `/matchup` tool with both champions and the role
   * already pinned. Optional so the row stays renderable without a destination,
   * in which case it is a plain `div` and carries no interaction affordance at
   * all (never a link-styled element that does nothing).
   */
  to?: string
}>()

// Win rate through the shared band, so this row, the tier-list chip, the
// champion directory and the matchup card all colour the same number the same
// way. It used to be a hand-rolled green/red — "the universal read", which it is
// everywhere except in a palette whose accent is itself a desaturated red.
const winRateClass = computed(() => winRateTone(props.entry.winRate))

// Lane win rate (#919) sits beside the game win rate, but it is a different
// measurement over a different denominator and must not read as a second opinion
// on the same one: it counts only *decided* lanes — a gold gap past the threshold
// at 15 minutes — so its sample is always smaller than `games`, and can be zero
// while `games` is not.
//
// A dash, never 0%, when there is nothing to say: no decided lane in this slice,
// or the live single-opponent search path, which has no lane data behind it at
// all. The tooltip carries the real decided-lane count, so the figure can never be
// read as resting on `games`.
const laneWinRateLabel = computed(() =>
  props.entry.laneWinRate === null ? '—' : formatPercentage(props.entry.laneWinRate, 0),
)
// `text-dimmed` for a null rather than `winRateTone`'s `text-muted`: an
// unmeasured lane is quieter still than a measured average one.
const laneWinRateClass = computed(() =>
  props.entry.laneWinRate === null ? 'text-dimmed' : winRateTone(props.entry.laneWinRate))
// The gold gap rides in the same tooltip (#976): it is the magnitude the rate
// cannot carry — 60% of lanes won by 120 gold and by 1200 are the same rate —
// but it rests on its own, smaller sample, so it is spelled out rather than
// squeezed into a second column that would read as a qualifier of the first.
const laneTooltip = computed(() => {
  const { laneWinRate, decidedLaneGames, averageGoldDiffAt15, goldDiffLaneGames } = props.entry
  const gap = averageGoldDiffAt15 === null
    ? null
    : `avg ${formatGoldDiff(averageGoldDiffAt15)} gold at 15 min over `
      + `${goldDiffLaneGames.toLocaleString()} lane(s)`
  if (laneWinRate === null) {
    return gap ?? 'No lane decided past the gold threshold at 15 min in this slice'
  }
  const rate = `Lane win rate over ${decidedLaneGames.toLocaleString()} decided lane(s)`
  return gap ? `${rate} · ${gap}` : rate
})

// A link when it leads somewhere, a plain div when it doesn't — never a
// link-styled element that does nothing.
//
// The component has to be *resolved here*, not named as a string in `:is`. A
// string only resolves to an intrinsic element or a component registered in this
// SFC's scope, and Nuxt's auto-import is a compile-time template transform that
// never sees a dynamic `:is` — so `:is="'NuxtLink'"` rendered a literal
// <nuxtlink> element: laid out correctly, styled correctly, and completely inert
// on click. Same idiom as leaderboard/ChampionBuild.vue and builder/GamesDrawer.vue.
const NuxtLinkComponent = resolveComponent('NuxtLink')

// The row's own sample, spelled out for the screen reader and the hover: "33
// games" alone does not say whether that is a matchup you see every other game
// or one you have met three times all split (#1082). The percentage is the
// quantity the backend's leaderboard floor is expressed in, so this is also the
// answer to "why is this opponent in the list and that one is not".
const gamesTooltip = computed(() => {
  const games = `${props.entry.games.toLocaleString()} game(s)`
  return props.entry.playRate > 0
    ? `${games} · ${formatPercentage(props.entry.playRate, 1)} of this champion's matchups`
    : games
})
</script>

<template>
  <component
    :is="to ? NuxtLinkComponent : 'div'"
    :to="to"
    :aria-label="to && opponent ? `Build against ${opponent.name}` : undefined"
    class="flex items-center gap-3 rounded-md px-2 py-1.5 transition-colors hover:bg-elevated/40"
    :class="to ? 'cursor-pointer focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-primary' : undefined"
  >
    <SkeletonImage
      v-if="opponent?.iconUrl"
      :src="opponent.iconUrl"
      :alt="opponent.name"
      width="32"
      height="32"
      loading="lazy"
      class="size-8 shrink-0 rounded"
    />
    <div v-else class="size-8 shrink-0 rounded bg-elevated" aria-hidden="true" />
    <span class="min-w-0 flex-1 truncate text-sm text-default">
      {{ opponent?.name ?? `Champion ${entry.opponentChampionId}` }}
    </span>
    <UTooltip :text="gamesTooltip">
      <span class="shrink-0 text-xs tabular-nums text-muted">
        {{ entry.games.toLocaleString() }} games
      </span>
    </UTooltip>
    <UTooltip :text="laneTooltip">
      <span
        class="w-12 shrink-0 text-right text-sm font-medium tabular-nums"
        :class="laneWinRateClass"
      >{{ laneWinRateLabel }}</span>
    </UTooltip>
    <span
      class="w-12 shrink-0 text-right text-sm font-semibold tabular-nums"
      :class="winRateClass"
    >
      {{ formatPercentage(entry.winRate, 0) }}
    </span>
  </component>
</template>
