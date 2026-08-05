<script setup lang="ts">
import type { ChampionPowerspikeEvent } from '~~/shared/types/champions'
import type { StaticItemData } from '~~/shared/types/static-data'
import { formatDuration } from '~/utils/relativeTime'

const props = withDefaults(defineProps<{
  events: ChampionPowerspikeEvent[]
  itemsMap: Record<number, StaticItemData>
  loading?: boolean
  // True when the filter bar has an opponent selected (#957) — the payload then
  // describes only the games of that matchup. It changes nothing about how the
  // bars are drawn, only what the copy is allowed to claim: "this champion" and
  // "this matchup" are two different populations, and the empty state has a
  // second cause here that it must not blame on the champion.
  matchupScoped?: boolean
}>(), {
  loading: false,
  matchupScoped: false,
})

// Two views over the same payload, switched client-side — no refetch, the
// response already carries both event kinds for this build.
type View = 'item' | 'level'
const VIEWS: { key: View, label: string }[] = [
  { key: 'item', label: 'Items' },
  { key: 'level', label: 'Levels' },
]
const selectedView = ref<View>('item')

// Only spikes that actually make the champion stronger. The magnitude is
// baseline-subtracted (excess over the power curve's ambient curvature), so
// positive means "accelerates the lead more than usual at that minute" — a
// negative value is noise, not a spike.
interface SpikeBar {
  key: string
  label: string
  item: StaticItemData | null
  level: number | null
  avgMinute: number
  spikeMagnitude: number
  games: number
}

// Server order is the build's core path order, and it is rendered as given
// (#1021). The previous "top 6 by magnitude, then re-sort by average minute" is
// gone on both counts: the payload now only carries this build's own core items,
// so there is nothing to rank away, and average minutes are per-item means over
// different sets of games — sorting by them fabricated a chronology the numbers
// never supported (two bars a minute apart described two disjoint cohorts, not
// two purchases). The path is capped server-side, so no client cap is needed.
const itemBars = computed<SpikeBar[]>(() =>
  props.events
    .filter(event => event.type === 'item' && event.spikeMagnitude > 0)
    .map(event => ({
      key: `item-${event.refId}`,
      label: props.itemsMap[event.refId]?.name ?? `Item ${event.refId}`,
      item: props.itemsMap[event.refId] ?? null,
      level: null,
      avgMinute: event.avgMinute,
      spikeMagnitude: event.spikeMagnitude,
      games: event.games,
    }))
    .filter(bar => bar.item !== null),
)

// Levels are the three ultimate-rank milestones, always in ascending order —
// they are a fixed scale, so ordering them by magnitude would misread.
const levelBars = computed<SpikeBar[]>(() =>
  props.events
    .filter(event => event.type === 'level' && event.spikeMagnitude > 0)
    .map(event => ({
      key: `level-${event.refId}`,
      label: `Level ${event.refId}`,
      item: null,
      level: event.refId,
      avgMinute: event.avgMinute,
      spikeMagnitude: event.spikeMagnitude,
      games: event.games,
    }))
    .sort((left, right) => (left.level ?? 0) - (right.level ?? 0)),
)

const bars = computed(() => selectedView.value === 'item' ? itemBars.value : levelBars.value)
const hasBars = computed(() => bars.value.length > 0)

// Bars are drawn by hand rather than with <ChartsBarChart>: unovis' band scale
// insets the first and last bar from the plot edges, which the full-width icon
// grid below does not, leaving them visibly misaligned (#761, #777). Heights are
// relative to the strongest spike in the active view; a floor keeps the weakest
// bar visible instead of collapsing it to a line.
const MIN_BAR_PERCENT = 8

const maxSpike = computed(() =>
  bars.value.reduce((max, bar) => Math.max(max, bar.spikeMagnitude), 0),
)

const barHeightPercent = (spike: number): number =>
  maxSpike.value <= 0
    ? MIN_BAR_PERCENT
    : MIN_BAR_PERCENT + (100 - MIN_BAR_PERCENT) * (spike / maxSpike.value)

const formatGameTime = (minutes: number): string => formatDuration(Math.round(minutes * 60))
const formatGames = (count: number): string => count.toLocaleString('en-US')

// Each bar's minute is a mean over its own games — the ones where that item was
// completed at all — so the bars do not share a denominator and the row is not a
// single game's timeline. The wording says "on average" and each bar carries its
// own sample, so the number is read as the per-item statistic it is rather than
// as a purchase clock. Conditioning the minutes on the preceding core items is
// #1022; until then the copy must not imply it already happens.
const tooltipFor = (bar: SpikeBar): string => {
  const when = bar.item
    ? `completed ~${formatGameTime(bar.avgMinute)} on average`
    : `reached ~${formatGameTime(bar.avgMinute)} on average`
  return `${bar.label} · +${bar.spikeMagnitude.toFixed(2)} lead acceleration · ${when} · ${formatGames(bar.games)} games`
}

// Spikes are folded per lane opponent only for matches ingested since #957, so a
// matchup can legitimately have no row at all while the champion at large has
// plenty. That is a different statement from "nothing spikes here", and saying the
// second when the first is true would read as a verdict on the matchup instead of
// an admission that we have not measured it. Distinguished on the raw payload, not
// on the filtered bars: a matchup with only negative spikes has been measured.
const hasMeasuredEvents = computed(() => props.events.length > 0)

const emptyMessage = computed(() => {
  if (props.matchupScoped && !hasMeasuredEvents.value) {
    return 'No game of this matchup has been measured for power spikes yet.'
  }

  const scope = props.matchupScoped ? 'this matchup' : 'this build'
  return selectedView.value === 'item'
    ? `No item shows a clear power spike in ${scope} yet.`
    : `No level milestone shows a clear power spike in ${scope} yet.`
})
</script>

<template>
  <SectionCard
    :level="3"
    title="Power spikes"
    :subtitle="matchupScoped
      ? `How much this build's completed items and level milestones accelerate the champion's lead, in the games it played this matchup.`
      : `How much this build's completed items and level milestones accelerate the champion's lead over its role opponent.`"
  >
    <template #actions>
      <div class="flex gap-1">
        <UButton
          v-for="view in VIEWS"
          :key="view.key"
          size="xs"
          :variant="selectedView === view.key ? 'soft' : 'ghost'"
          :color="selectedView === view.key ? 'primary' : 'neutral'"
          @click="() => { selectedView = view.key }"
        >
          {{ view.label }}
        </UButton>
      </div>
    </template>

    <USkeleton
      v-if="loading"
      class="h-40 w-full rounded-lg"
    />

    <p
      v-else-if="!hasBars"
      class="py-6 text-center text-sm text-muted"
    >
      {{ emptyMessage }}
    </p>

    <!-- Bar, icon and timing share one grid column each, so every bar sits
         exactly above the thing it measures. -->
    <div
      v-else
      class="grid items-end gap-2"
      :style="{ gridTemplateColumns: `repeat(${bars.length}, minmax(0, 1fr))` }"
    >
      <UTooltip
        v-for="bar in bars"
        :key="bar.key"
        :text="tooltipFor(bar)"
      >
        <div class="flex flex-col items-center gap-1">
          <div class="flex h-28 w-full items-end justify-center">
            <div
              class="w-6 rounded-t bg-primary transition-[height]"
              :style="{ height: `${barHeightPercent(bar.spikeMagnitude)}%` }"
            />
          </div>

          <GameTooltipItemIcon
            v-if="bar.item"
            :item="bar.item"
            :width="32"
            :height="32"
            class="size-8 rounded"
          />
          <span
            v-else
            class="flex size-8 items-center justify-center rounded-full bg-elevated text-xs font-semibold tabular-nums text-default"
          >
            {{ bar.level }}
          </span>

          <span class="text-xs font-medium tabular-nums text-muted">
            {{ formatGameTime(bar.avgMinute) }}
          </span>
        </div>
      </UTooltip>
    </div>
  </SectionCard>
</template>
