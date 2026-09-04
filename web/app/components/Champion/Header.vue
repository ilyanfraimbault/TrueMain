<script setup lang="ts">
import { POSITION_BY_VALUE } from '~/utils/positions'
import { roamVerdict } from '~/utils/roam-verdict'
import { formatPercentage } from '~~/shared/utils/ddragon'

const props = defineProps<{
  championName: string | null
  championIconUrl: string | null
  championId: number
  position: string
  totalGames: number
  totalWins: number
  /**
   * Which population the counts come from (#1466). The Truemains toggle sat in
   * the filter bar with its meaning available only on hover, so readers filtered
   * the whole page without ever learning what they had filtered. Saying it in
   * the stat line makes the toggle legible without a tooltip and without a
   * paragraph. Undefined where the page renders no toggle (the player-scoped
   * champion page), and the line then falls back to the bare count.
   */
  truemainsOnly?: boolean
  // Average out-of-lane kills + assists by 15 min (#536). Only ever surfaces as
  // a "Roamer" badge next to the win rate, and only for champions that clear the
  // threshold — see `roamVerdict`. Undefined where the page doesn't fetch it.
  roamKp15?: number | null
  // Thin-sample qualifier. When set, a warning icon sits next to the title with
  // this text in its tooltip — mirroring the builder's RecommendationPanel
  // rather than a full-width UAlert.
  lowSampleMessage?: string | null
  // The champion aggregate hasn't landed yet. Skeleton the title and the stat
  // line rather than rendering the props' placeholder values — those read as
  // real data ("Champion 164 · 0 games · 0.0% WR") instead of as a loading
  // state.
  loading?: boolean
}>()

const displayName = computed(() => props.championName ?? `Champion ${props.championId}`)
const winRate = computed(() => (props.totalGames === 0 ? 0 : props.totalWins / props.totalGames))

// The lane as its icon rather than the raw API value: `UTILITY` is Riot's
// internal name for support, and the glyph is what players actually read on a
// lane badge everywhere else on the site. The label stays available as the alt
// text (and in the tooltip) so it's never icon-only for a screen reader.
const positionOption = computed(() => POSITION_BY_VALUE.get(props.position) ?? null)

const roam = computed(() => roamVerdict(props.roamKp15))

// "played by mains" rather than "truemains only": next to a raw count the phrase
// has to read as a description of the games, not as the name of a control.
const populationLabel = computed(() => {
  if (props.truemainsOnly === undefined) return null
  return props.truemainsOnly ? 'played by mains' : 'across all tracked players'
})
</script>

<template>
  <div class="flex flex-1 flex-wrap items-center gap-4">
    <SkeletonImage
      :src="championIconUrl"
      :alt="championName ?? ''"
      width="80"
      height="80"
      class="size-20 rounded"
    />
    <div class="flex-1">
      <div class="flex h-8 items-center gap-2">
        <!-- Same height as the title it replaces, so the header doesn't jump
             when the name lands. -->
        <USkeleton
          v-if="loading && !championName"
          class="h-7 w-48"
        />
        <h1
          v-else
          class="text-2xl font-semibold"
        >
          {{ displayName }}
        </h1>
        <!-- The message lives in the tooltip so it never crowds the header. -->
        <UTooltip
          v-if="lowSampleMessage"
          :text="lowSampleMessage"
          :delay-duration="150"
        >
          <UIcon
            name="i-lucide-triangle-alert"
            class="size-5 text-warning"
          />
        </UTooltip>
      </div>
      <!-- A div, not a <p>: the lane tooltip renders interactive markup that
           has no business inside a paragraph. -->
      <!-- `min-h-5`, not a fixed height: it still reserves the skeleton's line
           so the header doesn't jump when the stats land, but the roam badge is
           allowed to be a hair taller than the text beside it. -->
      <div class="flex min-h-5 flex-wrap items-center gap-1.5 text-sm text-muted">
        <USkeleton
          v-if="loading"
          class="h-3.5 w-44"
        />
        <template v-else>
          <UTooltip
            v-if="positionOption"
            :text="positionOption.label"
            :delay-duration="150"
          >
            <SkeletonImage
              :src="positionOption.iconUrl"
              :alt="positionOption.label"
              :width="16"
              :height="16"
              class="size-4"
            />
          </UTooltip>
          <span v-else>—</span>
          <!-- A win rate over zero games is a fabricated 0.0%, not a
               measurement: on an empty slice (a lane the player never played,
               a patch with no game on record) the count stands alone. -->
          <span v-if="totalGames === 0">· no games on this slice</span>
          <!-- The population is interpolated with its own leading space rather
               than written as template whitespace: Vue condenses the whitespace
               between a text node and a `<template>`, which glued the label to
               the count ("115 gamesplayed by mains"). -->
          <span v-else>
            · {{ totalGames }} games<template v-if="populationLabel">{{ ` ${populationLabel}` }}</template>
            · {{ formatPercentage(winRate) }} WR
          </span>
          <!-- Playstyle flag, not a stat: it only appears for champions that
               actually roam, so it never competes with the numbers it sits next
               to. The measurement behind it lives in the tooltip. -->
          <UTooltip
            v-if="roam"
            :text="roam.tooltip"
            :delay-duration="150"
          >
            <UBadge
              color="primary"
              variant="soft"
              size="sm"
            >
              {{ roam.label }}
            </UBadge>
          </UTooltip>
        </template>
      </div>
    </div>
  </div>
</template>
