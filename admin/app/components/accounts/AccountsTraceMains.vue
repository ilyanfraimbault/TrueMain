<script setup lang="ts">
// Main champions card of the Trace tab: what MainAnalysis computed for the
// account, and what MainActivity did to it afterwards. Split out of
// `AccountsTrace.vue` (#1436).
import type { TableColumn } from '@nuxt/ui'
import type { AccountExplorerMainRow, AccountExplorerMainThresholds } from '~~/shared/types/ops'
import { formatDateTime, formatNumber, formatPercentOrDash } from '~~/shared/utils/format'
import { POSITION_BY_VALUE } from '~/utils/positions'

const props = defineProps<{
  rows: AccountExplorerMainRow[]
  thresholds: AccountExplorerMainThresholds | null
  /** When MainAnalysis last ran on the account — phrases the empty state. */
  lastMainCalcAtUtc: string | null | undefined
}>()

const { nameFor, iconFor } = useChampionStatic()

const mainColumns: TableColumn<AccountExplorerMainRow>[] = [
  { accessorKey: 'championId', header: 'Champion' },
  { accessorKey: 'championMatches', header: 'Games' },
  { accessorKey: 'playRate', header: 'Play rate' },
  { accessorKey: 'flags', header: 'Flags' },
  { accessorKey: 'primaryPosition', header: 'Position' },
  { accessorKey: 'calculatedAtUtc', header: 'Analysed' },
]

// Text fallback (tooltip, and rows with no breakdown at all) — the table cell
// itself renders an icon per position when the breakdown is available.
function positionSummary(row: AccountExplorerMainRow): string {
  if (row.positionBreakdown.length === 0) {
    return row.primaryPosition || '—'
  }
  return row.positionBreakdown
    .map(position => `${position.position} ${formatPercentOrDash(position.rate, 0)}`)
    .join(' · ')
}

// The absence sentence. "No rows" and "never ran" are different diagnoses and
// the card must not merge them.
const mainsEmptyNote = computed(() => {
  const lastRun = props.lastMainCalcAtUtc
  return lastRun
    ? `MainAnalysis has written no champion row for this account. It last ran on it ${traceStamp(lastRun)}, so the account was looked at and produced nothing above the sample floor.`
    : 'MainAnalysis has never run on this account, so the absence of champion rows is an absence of analysis — not a verdict that the player mains nothing.'
})
</script>

<template>
  <UCard :ui="{ body: 'p-0 sm:p-0' }" class="mb-8">
    <template #header>
      <PanelTitle
        title="Main champions"
        subtitle="What MainAnalysis computed, and what MainActivity did to it afterwards."
      >
        <template #info>
          <p v-if="thresholds">
            A champion is a main above a play rate somewhere between
            {{ formatPercentOrDash(thresholds.playRateFloor, 0) }} and
            {{ formatPercentOrDash(thresholds.playRateThreshold, 0) }}, and an OTP
            above {{ formatPercentOrDash(thresholds.otpPlayRateThreshold, 0) }}.
            {{ thresholds.effectiveThresholdNote }}
          </p>
          <p>
            "Not re-analysed" means MainAnalysis ran on the account more recently
            than it rewrote that row: its thin-sample guard declined to overwrite
            an established main from fewer than
            {{ formatNumber(thresholds?.minMatchesToEvaluate) }} matches. The row
            is deliberately old, not stale by accident.
          </p>
        </template>
      </PanelTitle>
    </template>

    <div v-if="rows.length === 0" class="px-4 py-8 text-sm text-muted">
      {{ mainsEmptyNote }}
    </div>

    <UTable
      v-else
      :data="rows"
      :columns="mainColumns"
      :ui="{ td: 'py-2' }"
    >
      <template #championId-cell="{ row }">
        <div class="flex items-center gap-2.5">
          <NuxtImg
            v-if="iconFor(row.original.championId)"
            :src="iconFor(row.original.championId)!"
            :alt="nameFor(row.original.championId)"
            width="28"
            height="28"
            loading="lazy"
            class="size-7 rounded-md ring-1 ring-default"
            :class="row.original.isActive ? '' : 'opacity-50'"
          />
          <div v-else class="size-7 rounded-md bg-elevated ring-1 ring-default" />
          <span class="font-medium text-highlighted">
            {{ nameFor(row.original.championId) }}
          </span>
        </div>
      </template>
      <template #championMatches-cell="{ row }">
        <span class="tabular-nums">
          {{ formatNumber(row.original.championMatches) }}
          <span class="text-dimmed">/ {{ formatNumber(row.original.totalMatches) }}</span>
        </span>
      </template>
      <template #playRate-cell="{ row }">
        <span class="tabular-nums">{{ formatPercentOrDash(row.original.playRate) }}</span>
      </template>
      <template #flags-cell="{ row }">
        <div class="flex flex-wrap items-center gap-1">
          <UBadge
            v-if="row.original.isMain"
            color="success"
            variant="subtle"
            size="sm"
            label="Main"
          />
          <UBadge v-if="row.original.isOtp" color="primary" variant="subtle" size="sm" label="OTP" />
          <UBadge
            v-if="row.original.isExtendedSample"
            color="info"
            variant="subtle"
            size="sm"
            label="Extended sample"
          />
          <UBadge
            v-if="!row.original.isActive"
            color="warning"
            variant="subtle"
            size="sm"
            icon="i-lucide-moon"
            label="Retired"
          />
          <UBadge
            v-if="row.original.analysisSkipped"
            color="neutral"
            variant="subtle"
            size="sm"
            label="Not re-analysed"
          />
        </div>
      </template>
      <template #primaryPosition-cell="{ row }">
        <UTooltip :text="positionSummary(row.original)">
          <div v-if="row.original.positionBreakdown.length > 0" class="flex items-center gap-1.5">
            <template v-for="stat in row.original.positionBreakdown" :key="stat.position">
              <NuxtImg
                v-if="POSITION_BY_VALUE.has(stat.position)"
                :src="POSITION_BY_VALUE.get(stat.position)!.iconUrl"
                :alt="POSITION_BY_VALUE.get(stat.position)!.label"
                width="16"
                height="16"
                class="size-4 shrink-0"
              />
              <span v-else class="text-xs text-muted uppercase">{{ stat.position }}</span>
            </template>
          </div>
          <span v-else class="text-xs text-muted">{{ positionSummary(row.original) }}</span>
        </UTooltip>
      </template>
      <template #calculatedAtUtc-cell="{ row }">
        <span class="text-xs tabular-nums">{{ formatDateTime(row.original.calculatedAtUtc) }}</span>
      </template>
    </UTable>

    <template #footer>
      <div class="space-y-2">
        <p
          v-for="row in rows.filter(candidateRow => candidateRow.deactivation)"
          :key="`deactivation-${row.championId}`"
          class="text-xs text-dimmed"
        >
          <strong>{{ nameFor(row.championId) }} retired.</strong>
          {{ row.deactivation!.reasonNote }}
          {{
            row.deactivation!.confirmedByActivityCheckAtUtc
              ? `Confirmed by a completed mastery check on ${formatDateTime(row.deactivation!.confirmedByActivityCheckAtUtc)}.`
              : 'No completed mastery check is on record for this account, so the retirement was never confirmed by one.'
          }}
        </p>
      </div>
    </template>
  </UCard>
</template>
