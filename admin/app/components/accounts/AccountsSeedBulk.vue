<script setup lang="ts">
// "Add in bulk" section of the Accounts hub's "Add mains" tab: paste Riot IDs one
// per line as `gameName#tagLine` (optionally `gameName#tagLine,REGION`), preview
// the parse, then "Seed all (N)". The run state lives in `useBulkSeed`; split out
// of `AccountsSeed.vue` (#1436).
import type { TableColumn } from '@nuxt/ui'
import type { BulkPreviewRow } from '~/utils/seed-bulk'
import { TRACKED_REGIONS } from '~~/shared/utils/regions'
import { bulkOutcomeBadge } from '~/utils/seed-bulk'

const emit = defineEmits<{
  /** A "seed all" settled: the queue's first page now holds the new requests. */
  finished: []
}>()

const {
  raw,
  defaultRegion,
  parsedRows,
  validRows,
  invalidCount,
  previewRows,
  running,
  doneCount,
  hasRun,
  summaryClean,
  summaryTitle,
  summaryDescription,
  progressPercent,
  seedAll,
  clearAll,
} = useBulkSeed(() => emit('finished'))

// Strictly-typed options for the default-region select so its model stays
// constrained to a `TrackedRegion`.
const bulkRegionItems = TRACKED_REGIONS.map(r => ({ label: r, value: r }))

const previewColumns: TableColumn<BulkPreviewRow>[] = [
  { accessorKey: 'gameName', header: 'Game name' },
  { accessorKey: 'tagLine', header: 'Tag' },
  { accessorKey: 'region', header: 'Region' },
  { accessorKey: 'outcome', header: 'Status' },
]

const previewTableMeta = {
  class: {
    tr: (row: { original: BulkPreviewRow }) => {
      if (!row.original.valid || row.original.outcome === 'failed') {
        return 'bg-error/5'
      }
      if (row.original.outcome === 'duplicate') {
        return 'bg-warning/5'
      }
      return ''
    },
  },
}
</script>

<template>
  <!-- Full-width so the preview table uses all available space. (The single-add
       form stays narrow; this section drives a table.) -->
  <section>
    <div class="flex items-center gap-2 mb-1">
      <UIcon name="i-lucide-clipboard-list" class="size-4 text-primary" />
      <h2 class="text-sm font-medium text-highlighted">
        Add in bulk
      </h2>
    </div>
    <p class="text-xs text-muted mb-4">
      One Riot ID per line as <code class="font-mono text-default">gameName#tagLine</code>.
      Append <code class="font-mono text-default">,REGION</code> to override the default region per line.
    </p>

    <div class="grid grid-cols-1 lg:grid-cols-[1fr_auto] gap-3 items-start mb-3">
      <UTextarea
        v-model="raw"
        :rows="8"
        :disabled="running"
        autoresize
        :maxrows="16"
        placeholder="Faker#KR1&#10;Caps#EUW,EUW1&#10;Doublelift#NA1,NA1"
        class="w-full font-mono"
        :ui="{ base: 'font-mono text-sm' }"
      />
      <div class="flex flex-row lg:flex-col gap-2 lg:w-44">
        <UFormField label="Default region" class="w-full">
          <USelect
            v-model="defaultRegion"
            :items="bulkRegionItems"
            icon="i-lucide-globe"
            :disabled="running"
            class="w-full"
          />
        </UFormField>
      </div>
    </div>

    <!-- Counts -->
    <div class="flex flex-wrap items-center gap-2 mb-3">
      <UBadge
        color="success"
        variant="subtle"
        size="sm"
        :icon="'i-lucide-circle-check'"
        :label="`${validRows.length} valid`"
      />
      <UBadge
        v-if="invalidCount > 0"
        color="error"
        variant="subtle"
        size="sm"
        icon="i-lucide-circle-x"
        :label="`${invalidCount} skipped`"
      />
      <span v-if="parsedRows.length === 0" class="text-xs text-dimmed">
        Nothing parsed yet.
      </span>
    </div>

    <!-- Actions -->
    <div class="flex flex-wrap items-center gap-3 mb-4">
      <UButton
        icon="i-lucide-upload"
        :label="`Seed all (${validRows.length})`"
        :loading="running"
        :disabled="validRows.length === 0 || running"
        @click="seedAll"
      />
      <UButton
        icon="i-lucide-trash-2"
        color="neutral"
        variant="ghost"
        label="Clear"
        :disabled="running || raw.length === 0"
        @click="clearAll"
      />
    </div>

    <!-- Progress while running -->
    <div v-if="running" class="mb-4">
      <div class="flex items-center justify-between text-xs text-muted mb-1.5">
        <span>Seeding {{ doneCount }} / {{ validRows.length }}…</span>
        <span class="tabular-nums">{{ progressPercent }}%</span>
      </div>
      <UProgress :model-value="progressPercent" :max="100" color="primary" />
    </div>

    <!-- Final summary -->
    <UAlert
      v-else-if="hasRun"
      :color="summaryClean ? 'success' : 'warning'"
      variant="subtle"
      :icon="summaryClean ? 'i-lucide-circle-check' : 'i-lucide-triangle-alert'"
      :title="summaryTitle"
      :description="`${summaryDescription}. Matches & mains follow on the next ingestion run.`"
      class="mb-4"
    />

    <!-- Preview / result table -->
    <UCard v-if="previewRows.length > 0" :ui="{ body: 'p-0 sm:p-0' }">
      <template #header>
        <p class="text-sm font-medium text-highlighted">
          Preview
        </p>
      </template>

      <UTable
        :data="previewRows"
        :columns="previewColumns"
        :meta="previewTableMeta"
        :ui="{ td: 'py-2' }"
      >
        <template #gameName-cell="{ row }">
          <span class="font-medium text-highlighted">{{ row.original.gameName || '—' }}</span>
        </template>
        <template #tagLine-cell="{ row }">
          <span class="text-muted font-mono text-xs">
            {{ row.original.tagLine ? `#${row.original.tagLine}` : '—' }}
          </span>
        </template>
        <template #region-cell="{ row }">
          <span class="text-muted font-mono text-xs">{{ row.original.region }}</span>
        </template>
        <template #outcome-cell="{ row }">
          <div class="flex items-center gap-2">
            <UBadge
              :color="bulkOutcomeBadge(row.original).color"
              :icon="bulkOutcomeBadge(row.original).icon"
              variant="subtle"
              size="sm"
              :label="bulkOutcomeBadge(row.original).label"
              :ui="{ leadingIcon: row.original.outcome === 'queued' ? 'animate-spin' : '' }"
            />
            <span
              v-if="row.original.error"
              class="text-error text-xs line-clamp-1 max-w-xs"
              :title="row.original.error"
            >
              {{ row.original.error }}
            </span>
          </div>
        </template>

        <template #empty>
          <div class="py-10 text-center text-sm text-muted">
            Paste some Riot IDs above to preview them.
          </div>
        </template>
      </UTable>
    </UCard>
  </section>
</template>
