<script setup lang="ts">
// Rank snapshots card of the Trace tab. Split out of `AccountsTrace.vue` (#1436).
import type { TableColumn } from '@nuxt/ui'
import type { AccountExplorerRankSnapshot } from '~~/shared/types/ops'
import { formatDateTime, formatNumber } from '~~/shared/utils/format'

const props = defineProps<{
  snapshots: AccountExplorerRankSnapshot[]
  /** When AccountRefresh last read league-v4 for the account — phrases the empty state. */
  lastRankSyncAtUtc: string | null | undefined
}>()

const rankColumns: TableColumn<AccountExplorerRankSnapshot>[] = [
  { accessorKey: 'capturedAtUtc', header: 'Captured' },
  { accessorKey: 'tier', header: 'Rank' },
  { accessorKey: 'leaguePoints', header: 'LP' },
  { accessorKey: 'wins', header: 'W–L' },
]

// "Checked and unranked" and "never checked" are different diagnoses.
const rankEmptyNote = computed(() => {
  const lastSync = props.lastRankSyncAtUtc
  return lastSync
    ? `No rank snapshot on record. AccountRefresh last read league-v4 for this account ${traceStamp(lastSync)}, so the account was checked and came back unranked in solo queue.`
    : 'No rank snapshot on record, and AccountRefresh has never completed a league-v4 read for this account — so this is missing data, not a missing rank.'
})
</script>

<template>
  <UCard :ui="{ body: 'p-0 sm:p-0' }" class="mb-8">
    <template #header>
      <PanelTitle
        title="Rank snapshots"
        subtitle="Most recent first."
        info="Solo queue only, at most one row per UTC day, and never pruned — so a
          gap here is a gap in play, not in storage."
      />
    </template>

    <div v-if="snapshots.length === 0" class="px-4 py-8 text-sm text-muted">
      {{ rankEmptyNote }}
    </div>

    <UTable
      v-else
      :data="snapshots"
      :columns="rankColumns"
      :ui="{ td: 'py-2' }"
    >
      <template #capturedAtUtc-cell="{ row }">
        <span class="text-xs tabular-nums">{{ formatDateTime(row.original.capturedAtUtc) }}</span>
      </template>
      <template #tier-cell="{ row }">
        <span class="text-sm text-highlighted">
          {{ row.original.tier }} {{ row.original.division }}
        </span>
      </template>
      <template #leaguePoints-cell="{ row }">
        <span class="tabular-nums">{{ formatNumber(row.original.leaguePoints) }}</span>
      </template>
      <template #wins-cell="{ row }">
        <span class="text-xs tabular-nums">
          {{ formatNumber(row.original.wins) }}–{{ formatNumber(row.original.losses) }}
        </span>
      </template>
    </UTable>
  </UCard>
</template>
