<script setup lang="ts">
/**
 * One figure on a hero: a micro-label, the value, and how it moved — the
 * dashboard's tile (#1682), shared so the champion page and the player profile
 * draw the same pane. The material is the dashboard's exactly: `ink-950` at
 * 55 %, a white hairline and a blur, so the splash behind it shows through.
 *
 * `delta` is the movement against a reference the caller names in `hint`
 * (the previous patch, the player's own average); its `good` says which way
 * the data axis reads it, null for a move worth no colour. `series` draws the
 * run of form under the figure; without one the tile ends on its `foot` line.
 * `reference` is a second yardstick beside the move: the median of the
 * player's tier on their main role (#2076), the tier's emblem and the figure,
 * with what it was read from in its tooltip.
 */
const props = defineProps<{
  label: string
  value: string
  delta?: { text: string, direction: 'up' | 'down', good: boolean | null } | null
  hint?: string
  series?: number[]
  foot?: string
  loading?: boolean
  reference?: { text: string, tier: string, hint: string } | null
}>()

const tone = computed(() => {
  const good = props.delta?.good
  if (good === undefined || good === null) return 'flat'
  return good ? 'good' : 'bad'
})
</script>

<template>
  <div class="flex min-w-0 flex-col gap-1.5 rounded-lg bg-ink-950/55 px-3 pb-2 pt-2.5 ring-1 ring-white/5 backdrop-blur-md">
    <div class="flex min-w-0 items-center justify-between gap-2">
      <span class="stat-label truncate">{{ label }}</span>
      <UTooltip
        v-if="reference && !loading"
        :text="reference.hint"
        :delay-duration="150"
      >
        <span
          class="inline-flex shrink-0 items-center gap-1 text-[11px] font-medium tabular-nums text-muted"
          data-testid="kpi-reference"
          :aria-label="reference.hint"
        >
          <RankIcon
            :tier="reference.tier"
            :size="14"
          />
          {{ reference.text }}
        </span>
      </UTooltip>
    </div>
    <USkeleton
      v-if="loading"
      class="h-5 w-16 bg-ink-950/55"
    />
    <div
      v-else
      class="flex items-baseline justify-between gap-2"
    >
      <span class="stat-value text-xl leading-none">{{ value }}</span>
      <UTooltip
        v-if="delta"
        :text="hint"
        :disabled="!hint"
        :delay-duration="150"
      >
        <span class="inline-flex items-center gap-1 text-xs font-semibold tabular-nums text-default">
          <UIcon
            :name="delta.direction === 'up' ? 'i-lucide-trending-up' : 'i-lucide-trending-down'"
            class="size-3.5"
            :class="tone === 'good' ? 'text-data-good' : tone === 'bad' ? 'text-data-bad' : 'text-muted'"
          />
          {{ delta.text }}
        </span>
      </UTooltip>
    </div>
    <KpiSparkline
      v-if="series"
      :values="series"
      :tone="tone"
    />
    <p
      v-else-if="foot"
      class="truncate text-[11px] text-muted"
    >
      {{ foot }}
    </p>
  </div>
</template>
