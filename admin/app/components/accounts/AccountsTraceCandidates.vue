<script setup lang="ts">
// Candidate funnel card of the Trace tab: the account's `main_candidates` rows and
// the manual seed request that brought it in, if any. Split out of
// `AccountsTrace.vue` (#1436).
import type { TableColumn } from '@nuxt/ui'
import type { AccountExplorerCandidate, SeedRequestReadModel } from '~~/shared/types/ops'
import { formatDateTime, formatNumber } from '~~/shared/utils/format'

defineProps<{
  candidates: AccountExplorerCandidate[]
  seedRequest: SeedRequestReadModel | null
}>()

const { nameFor, iconFor } = useChampionStatic()

const candidateColumns: TableColumn<AccountExplorerCandidate>[] = [
  { accessorKey: 'championId', header: 'Champion' },
  { accessorKey: 'status', header: 'Status' },
  { accessorKey: 'source', header: 'Source' },
  { accessorKey: 'score', header: 'Score' },
  { accessorKey: 'scoreInputs', header: 'Score inputs' },
  { accessorKey: 'discoveredAtUtc', header: 'Discovered' },
  { accessorKey: 'validatedAtUtc', header: 'Validated' },
]

// The score inputs a candidate actually carries depend on where it came from:
// ladder rows hold mastery rank/points, harvest rows hold observed games. Show
// the ones its source populated rather than a row of zeros for the others.
function scoreInputsLabel(candidate: AccountExplorerCandidate): string {
  const inputs = candidate.scoreInputs
  const parts = [`last played ${formatDateTime(inputs.lastPlayTimeUtc)}`]
  if (candidate.source === 'Harvest') {
    parts.push(`${formatNumber(inputs.observedGames)} observed games`)
  }
  else {
    parts.push(`mastery #${formatNumber(inputs.championRankInMasteryTop)}`)
    parts.push(`${formatNumber(inputs.championPoints)} pts`)
  }
  return parts.join(' · ')
}

// The absence sentence: says what did not happen rather than a bare empty table.
const candidatesEmptyNote = 'No main_candidates row exists for this account. Note that candidates are keyed on (platformId, puuid) and carry no Riot ID of their own, so a candidate whose account has not been upserted yet would be invisible to this search.'
</script>

<template>
  <UCard :ui="{ body: 'p-0 sm:p-0' }" class="mb-8">
    <template #header>
      <PanelTitle
        title="Candidate funnel"
        subtitle="New → Scored → Queued → Processing → Validated."
      >
        <template #info>
          <p>
            The score's <strong>components are not persisted</strong> — only the
            final blend is — so the inputs listed are what can be shown.
            Recomputing recency / rank / points / scarcity here would fold today's
            champion coverage into a number produced against an older snapshot,
            and would silently disagree with the score beside it.
          </p>
          <p>
            A source of <code>Ladder</code> does not rule out a manual seed:
            ManualSeedProcess reuses the ladder upsert, so
            <code>ManualSeed</code> is never assigned in production. The seed
            request below is the reliable trail.
          </p>
        </template>
      </PanelTitle>
    </template>

    <div
      v-if="candidates.length === 0"
      class="px-4 py-8 text-sm text-muted"
    >
      {{ candidatesEmptyNote }}
    </div>

    <template v-else>
      <UTable
        :data="candidates"
        :columns="candidateColumns"
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
            />
            <div v-else class="size-7 rounded-md bg-elevated ring-1 ring-default" />
            <span class="font-medium text-highlighted">
              {{ nameFor(row.original.championId) }}
            </span>
          </div>
        </template>
        <template #status-cell="{ row }">
          <UBadge
            :color="candidateStatusColor(row.original.status)"
            variant="subtle"
            size="sm"
            :icon="candidateStatusIcon(row.original.status)"
            :label="row.original.status"
          />
        </template>
        <template #source-cell="{ row }">
          <span class="text-xs text-muted">{{ row.original.source }}</span>
        </template>
        <template #score-cell="{ row }">
          <span class="tabular-nums">{{ row.original.score.toFixed(3) }}</span>
        </template>
        <template #scoreInputs-cell="{ row }">
          <span class="text-xs text-muted tabular-nums">
            {{ scoreInputsLabel(row.original) }}
          </span>
        </template>
        <template #discoveredAtUtc-cell="{ row }">
          <span class="text-xs tabular-nums">{{ formatDateTime(row.original.discoveredAtUtc) }}</span>
        </template>
        <template #validatedAtUtc-cell="{ row }">
          <span class="text-xs tabular-nums">{{ formatDateTime(row.original.validatedAtUtc) }}</span>
        </template>
      </UTable>
    </template>

    <template v-if="seedRequest" #footer>
      <PanelTitle variant="label" title="Manual seed request" class="mb-2" />
      <div class="flex flex-wrap items-center gap-2 mb-2">
        <UBadge
          :color="seedStatusColor(seedRequest.status)"
          variant="subtle"
          size="sm"
          :icon="seedStatusIcon(seedRequest.status)"
          :label="seedRequest.status"
        />
        <span class="font-mono text-xs text-muted">{{ seedRequest.platformId }}</span>
        <span class="text-xs text-dimmed tabular-nums">
          requested {{ formatDateTime(seedRequest.requestedAtUtc) }} ·
          processed {{ formatDateTime(seedRequest.processedAtUtc) }}
        </span>
      </div>
      <UAlert
        v-if="seedRequest.error"
        color="error"
        variant="subtle"
        icon="i-lucide-triangle-alert"
        title="Seed request failed"
        :description="seedRequest.error"
      />
    </template>
  </UCard>
</template>
