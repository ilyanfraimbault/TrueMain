<script setup lang="ts">
import type { TruemainDedication } from '#shared/types/dedication'

// A truemain's score on their signature champion (#1701), with the verdict,
// the facts and the points each one added on hover. Every figure comes from
// the API payload — the parts are never recomputed here, so the tooltip can't
// drift from the number the backend ranked on.
//
// Attributes land on the trigger, not the tooltip: the champion page's rows
// pass `relative z-10` there to lift it above their stretched profile link.
defineOptions({ inheritAttrs: false })

const props = defineProps<{
  dedication: TruemainDedication
  championName: string
  /** The list is ranked by this score: the figure is underlined as the sort key. */
  highlighted?: boolean
  /** A "Score" caption under the figure, for a row without a column header. */
  labelled?: boolean
}>()

const score = computed(() => formatDedicationScore(props.dedication.score))
const lastPlayed = computed(() => formatDedicationLastPlayed(props.dedication.daysSinceLastPlayed))

const TOOLTIP_UI = { content: 'p-0 h-auto max-w-none bg-transparent ring-0 shadow-none text-default' }
</script>

<template>
  <UTooltip :delay-duration="150" :ui="TOOLTIP_UI">
    <div class="flex flex-col items-end" data-testid="truemain-score" v-bind="$attrs">
      <span
        class="font-semibold tabular-nums"
        :class="{ 'underline decoration-dotted underline-offset-2': highlighted }"
      >{{ score }}</span>
      <span v-if="labelled" class="stat-label mt-0.5">Score</span>
    </div>

    <template #content>
      <GameTooltipSurface>
        <div class="mb-2 flex w-64 items-center gap-2">
          <p class="min-w-0 flex-1 truncate text-xs font-semibold text-default">
            {{ TRUEMAIN_SCORE_LABEL }} {{ score }} · {{ championName }}
          </p>
          <DedicationVerdict :dedication="dedication" />
        </div>
        <DedicationBreakdown :parts="dedicationParts(dedication)" />
        <p v-if="lastPlayed" class="mt-2 text-[10px] text-muted">
          {{ lastPlayed }}
        </p>
      </GameTooltipSurface>
    </template>
  </UTooltip>
</template>
