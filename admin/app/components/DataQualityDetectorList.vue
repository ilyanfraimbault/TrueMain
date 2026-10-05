<script setup lang="ts">
// The Data Quality page's automated detectors (#924): what the database says about
// itself, severity-ordered, one line each. Thresholds, source notes and healthy
// rows sit behind a per-detector expand (`DataQualityDetectorItem`), and the
// passing checks are collapsed by default.
import type { DataQualityDetector, DetectorStatus } from '~~/shared/types/ops'

const props = defineProps<{
  detectors: DataQualityDetector[]
  pending: boolean
  error: unknown
}>()

const emit = defineEmits<{ drillDown: [] }>()

// Worst first. `unknown` outranks green for the same reason it does on the
// backend — a summary must not read clean when part of it was never measured —
// but stays below amber: it is a gap in the audit, not a failure.
const STATUS_RANK: Record<DetectorStatus, number> = { red: 0, amber: 1, unknown: 2, green: 3 }
const orderedDetectors = computed(
  () => [...props.detectors].sort((a, b) => STATUS_RANK[a.status] - STATUS_RANK[b.status]),
)
const failingDetectors = computed(() => orderedDetectors.value.filter(d => d.status !== 'green'))
const passingDetectors = computed(() => orderedDetectors.value.filter(d => d.status === 'green'))

// Passing checks are collapsed by default: on a healthy corpus the section is
// one line, and the operator's attention is never spent on the four checks that
// found nothing.
const showPassing = ref(false)
const listedDetectors = computed(
  () => (showPassing.value ? orderedDetectors.value : failingDetectors.value),
)
</script>

<template>
  <section class="mb-10">
    <h2 class="mb-3 text-sm font-medium text-highlighted">
      Automated checks
    </h2>

    <FetchErrorAlert
      v-if="error"
      :error="error"
      title="Failed to load the automated detectors"
    />

    <div v-else-if="pending && detectors.length === 0" class="space-y-3">
      <USkeleton v-for="n in 3" :key="n" class="h-14 w-full" />
    </div>

    <UCard v-else :ui="{ body: 'py-2' }">
      <div class="divide-y divide-default">
        <DataQualityDetectorItem
          v-for="detector in listedDetectors"
          :key="detector.key"
          :detector="detector"
          drill-down-label="Per-champion breakdown"
          @drill-down="emit('drillDown')"
        />
      </div>

      <div v-if="passingDetectors.length > 0" class="pt-2" :class="listedDetectors.length > 0 ? 'border-t border-default mt-2' : ''">
        <UButton
          size="xs"
          color="neutral"
          variant="ghost"
          :icon="showPassing ? 'i-lucide-chevron-up' : 'i-lucide-chevron-down'"
          :label="showPassing
            ? 'Hide passing checks'
            : `Show ${passingDetectors.length} passing ${pluralize(passingDetectors.length, 'check', 'checks')}`"
          @click="void (showPassing = !showPassing)"
        />
      </div>
    </UCard>
  </section>
</template>
