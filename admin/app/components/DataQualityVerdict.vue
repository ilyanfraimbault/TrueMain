<script setup lang="ts">
// The Data Quality page's verdict: the one line the panel exists to produce,
// answering "is the database healthy right now?" from the automated detectors,
// with the flagged-match count under the active filters next to it.
import type { DataQualityDetectorsResponse } from '~~/shared/types/ops'
import { formatDateTime } from '~~/shared/utils/format'

const props = defineProps<{
  detectorsData: DataQualityDetectorsResponse | null | undefined
  pending: boolean
  failed: boolean
  /** The flagged-match total, or `null` while that list is loading or failed. */
  flaggedTotal: number | null
  hasActiveFilters: boolean
}>()

const detectors = computed(() => props.detectorsData?.detectors ?? [])

/**
 * The single line the panel exists to produce. Worded from the counts so it can
 * never claim clean while something is red, and neutral — not green — when there
 * is nothing to report on.
 */
const verdict = computed<{ tone: 'success' | 'warning' | 'error' | 'neutral', icon: string, title: string }>(() => {
  const counts = { red: 0, amber: 0, unknown: 0, green: 0 }
  for (const detector of detectors.value) {
    counts[detector.status]++
  }

  if (detectors.value.length === 0) {
    return { tone: 'neutral', icon: 'i-lucide-circle-help', title: 'No automated checks reported' }
  }
  if (counts.red > 0) {
    return {
      tone: 'error',
      icon: 'i-lucide-octagon-alert',
      title: `${counts.red} ${pluralize(counts.red, 'check is', 'checks are')} failing`,
    }
  }
  if (counts.amber > 0) {
    return {
      tone: 'warning',
      icon: 'i-lucide-triangle-alert',
      title: `${counts.amber} ${pluralize(counts.amber, 'check needs', 'checks need')} attention`,
    }
  }
  if (counts.unknown > 0) {
    return {
      tone: 'neutral',
      icon: 'i-lucide-circle-help',
      title: `${counts.unknown} ${pluralize(counts.unknown, 'check', 'checks')} could not be measured`,
    }
  }
  return {
    tone: 'success',
    icon: 'i-lucide-shield-check',
    title: `All ${detectors.value.length} checks pass`,
  }
})

const VERDICT_TONE_CLASS: Record<'success' | 'warning' | 'error' | 'neutral', string> = {
  success: 'text-success',
  warning: 'text-warning',
  error: 'text-error',
  neutral: 'text-muted',
}
</script>

<template>
  <div v-if="pending && detectors.length === 0" class="mb-8">
    <USkeleton class="h-12 w-full max-w-md" />
  </div>
  <div v-else-if="!failed" class="mb-8 flex items-start gap-3">
    <UIcon
      :name="verdict.icon"
      class="size-6 shrink-0"
      :class="VERDICT_TONE_CLASS[verdict.tone]"
    />
    <div class="min-w-0">
      <p class="text-base font-medium" :class="VERDICT_TONE_CLASS[verdict.tone]">
        {{ verdict.title }}
      </p>
      <p class="mt-0.5 text-xs text-muted">
        {{ detectors.length }} automated {{ pluralize(detectors.length, 'check', 'checks') }}<template
          v-if="detectorsData"
        > · evaluated {{ formatDateTime(detectorsData.evaluatedAtUtc) }}</template><template
          v-if="flaggedTotal !== null"
        > · {{ flaggedTotal.toLocaleString('en-US') }} flagged {{ pluralize(flaggedTotal, 'match', 'matches') }}
          {{ hasActiveFilters ? 'under the active filters' : 'in the scanned window' }}</template>
      </p>
    </div>
  </div>
</template>
