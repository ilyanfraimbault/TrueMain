<script setup lang="ts">
import { formatPercentage } from '~~/shared/utils/ddragon'
import { POSITION_BY_VALUE } from '#common/utils/positions'

/**
 * The champion section of the player-scoped champion page: the banner above is
 * the player's (the profile's own), so the champion the page is scoped to opens
 * the main column — its portrait, name, the lane and the player's sample on the
 * slice, with the thin-sample warning.
 */
const props = withDefaults(defineProps<{
  championName: string | null
  championIconUrl: string | null
  championId: number
  position: string
  totalGames: number
  totalWins?: number | null
  lowSampleMessage?: string | null
  loading?: boolean
}>(), { totalWins: null, lowSampleMessage: null, loading: false })

const positionOption = computed(() => POSITION_BY_VALUE.get(props.position) ?? null)
// A win rate over zero games would be a fabricated 0%, so an empty slice keeps
// its own line and never reaches this.
const winRateLabel = computed(() =>
  props.totalWins == null ? '' : ` · ${formatPercentage(props.totalWins / props.totalGames, 0)} WR`)
</script>

<template>
  <header class="flex items-center gap-3">
    <SkeletonImage
      :src="championIconUrl"
      :alt="championName ?? ''"
      width="44"
      height="44"
      class="size-11 shrink-0 rounded-lg ring-1 ring-white/10"
    />
    <div class="min-w-0">
      <USkeleton
        v-if="loading && !championName"
        class="h-6 w-40"
      />
      <h2
        v-else
        class="truncate text-lg font-semibold leading-tight text-highlighted"
      >
        {{ championName ?? `Champion ${championId}` }} build
      </h2>
      <div class="mt-0.5 flex flex-wrap items-center gap-2 text-sm text-muted">
        <USkeleton
          v-if="loading"
          class="h-4 w-36"
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
              transparent
              :width="16"
              :height="16"
              class="size-4"
            />
          </UTooltip>
          <span v-if="totalGames === 0">No games on this slice</span>
          <span v-else>{{ totalGames.toLocaleString('en-US') }} games{{ winRateLabel }}</span>
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
      </div>
    </div>
  </header>
</template>
