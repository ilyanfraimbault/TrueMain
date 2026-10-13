<script setup lang="ts">
import { formatCount } from '#shared/utils/counts'
import { formatPointDelta } from '#shared/utils/deltas'
import { championDisplayName } from '#shared/utils/champion-name'
import type { ChampionTrendPoint } from '#shared/types/champions'
import { formatPercentage, formatPercentageOrDash } from '#shared/utils/ddragon'
import { POSITION_BY_VALUE } from '#common/utils/positions'

/**
 * The champion page's banner: the champion's splash under its portrait and
 * name, the lane and the sample the page reads, its tier on this slice, and the
 * patch at a glance — win, pick and ban rate, each against the patch before,
 * and the games behind them. The dashboard's banner (`PageHero`), shared by the
 * site and the app.
 *
 * The four figures come from the trend series (`/champions/{id}/trend`), which
 * takes no rank filter: they describe the lane across every rank, which the
 * tooltip on each move says, while the sample line under the name is the
 * page's own slice.
 *
 * The one champion banner (#2026): the champion page, the player-scoped
 * champion page and the desktop app's champion page all draw it. What differs
 * comes in as props and slots — the player-scoped page passes its player's wins
 * (the line then carries their win rate) and no trend, since its numbers are
 * one player's and the trend's are every rank; the app swaps the lane icon for
 * its lane picker through `#lane`.
 */
const props = withDefaults(defineProps<{
  championName: string | null
  championIconUrl: string | null
  championId: number
  splashUrl: string | null
  position: string
  /** The patch the page reads; the trend point it names is the current one. */
  patch?: string | null
  totalGames: number
  /** Wins on the slice: given, the sample line ends on its win rate (whole percent, #1469). */
  totalWins?: number | null
  /** Which population the sample counts (#1466); undefined where no toggle exists. */
  truemainsOnly?: boolean
  lowSampleMessage?: string | null
  tier?: string | null
  /**
   * The patch figures' series. Omitted, the banner has no figures row; null
   * while it loads (or where there is none to read), the tiles show dashes.
   */
  trend?: ChampionTrendPoint[] | null
  loading?: boolean
  trendLoading?: boolean
}>(), { truemainsOnly: undefined, totalWins: null, patch: null, lowSampleMessage: null, tier: null, trend: undefined })

const displayName = computed(() => championDisplayName(props.championId, props.championName))
const positionOption = computed(() => POSITION_BY_VALUE.get(props.position) ?? null)
const populationLabel = computed(() => {
  if (props.truemainsOnly === undefined) return ''
  return props.truemainsOnly ? ' played by mains' : ' across all tracked players'
})
// A win rate over zero games would be a fabricated 0%, so an empty slice keeps
// its own line ("No games on this slice") and never reaches this.
const winRateLabel = computed(() =>
  props.totalWins == null ? '' : ` · ${formatPercentage(props.totalWins / props.totalGames, 0)} WR`)

const points = computed(() => props.trend ?? [])
const currentIndex = computed(() => {
  const index = props.patch ? points.value.findIndex(point => point.patch === props.patch) : -1
  return index >= 0 ? index : points.value.length - 1
})
const current = computed(() => points.value[currentIndex.value] ?? null)
const previous = computed(() => points.value[currentIndex.value - 1] ?? null)

type Delta = { text: string, direction: 'up' | 'down', good: boolean | null }
function move(now: number | null | undefined, before: number | null | undefined, higherIsGood: boolean | null): Delta | null {
  if (now == null || before == null) return null
  const delta = now - before
  return {
    // Unsigned: the arrow gives the direction.
    text: formatPointDelta(delta, { signed: false }),
    direction: delta >= 0 ? 'up' : 'down',
    good: higherIsGood === null ? null : (delta >= 0) === higherIsGood,
  }
}
const hint = computed(() => previous.value ? `Every rank, against patch ${previous.value.patch}` : undefined)

const tiles = computed(() => {
  const now = current.value
  const before = previous.value
  return [
    { label: 'Win rate', value: now ? formatPercentage(now.winRate) : '—', delta: move(now?.winRate, before?.winRate, true) },
    { label: 'Pick rate', value: now ? formatPercentage(now.pickRate) : '—', delta: move(now?.pickRate, before?.pickRate, true) },
    { label: 'Ban rate', value: formatPercentageOrDash(now?.banRate), delta: move(now?.banRate, before?.banRate, null) },
    { label: 'Games', value: now ? formatCount(now.games) : '—', delta: null },
  ]
})
</script>

<template>
  <PageHero
    :splash-url="splashUrl"
    splash-position="60% 20%"
  >
    <template #portrait>
      <SkeletonImage
        :src="championIconUrl"
        :alt="championName ?? ''"
        width="72"
        height="72"
        class="size-[72px] shrink-0 rounded-lg shadow-lg ring-1 ring-white/10"
      />
    </template>

    <template #title>
      <USkeleton
        v-if="loading && !championName"
        class="h-8 w-48"
      />
      <h1
        v-else
        class="truncate text-[28px] font-semibold leading-tight text-highlighted"
      >
        {{ displayName }}
      </h1>
    </template>

    <template #subtitle>
      <USkeleton
        v-if="loading"
        class="h-4 w-44"
      />
      <template v-else>
        <slot name="lane">
          <UTooltip
            v-if="positionOption"
            :text="positionOption.label"
            :delay-duration="150"
          >
            <SkeletonImage
              :src="positionOption.iconUrl"
              :alt="positionOption.label"
              transparent
              :width="16"
              :height="16"
              class="size-4"
            />
          </UTooltip>
        </slot>
        <span v-if="totalGames === 0">No games on this slice</span>
        <span v-else>{{ formatCount(totalGames) }} games{{ populationLabel }}{{ winRateLabel }}</span>
        <UTooltip
          v-if="lowSampleMessage"
          :text="lowSampleMessage"
          :delay-duration="150"
        >
          <UIcon
            name="i-lucide-triangle-alert"
            class="size-4 text-warning"
          />
        </UTooltip>
      </template>
    </template>

    <template #actions>
      <UBadge
        v-if="tier"
        variant="soft"
        size="lg"
        class="gap-2 bg-ink-950/60 ring ring-inset ring-white/10 backdrop-blur-md"
      >
        <TierBadge :tier="tier" />
        <span class="text-[10px] font-medium uppercase tracking-[0.1em] text-muted">Tier</span>
      </UBadge>
      <slot name="actions" />
    </template>

    <!-- A conditional slot, not a v-if inside it: PageHero draws its figures
         row whenever the default slot is passed at all. -->
    <template
      v-if="trend !== undefined"
      #default
    >
      <KpiTile
        v-for="tile in tiles"
        :key="tile.label"
        :label="tile.label"
        :value="tile.value"
        :delta="tile.delta"
        :hint="hint"
        :loading="trendLoading && !current"
      />
    </template>
  </PageHero>
</template>
