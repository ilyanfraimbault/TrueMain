<script setup lang="ts">
// Identity card of the Trace tab: when each half of the pipeline last touched the
// account. Split out of `AccountsTrace.vue` (#1436).
import type { AccountExplorerIdentity } from '~~/shared/types/ops'
import { formatNumber } from '~~/shared/utils/format'

defineProps<{
  identity: AccountExplorerIdentity
}>()
</script>

<template>
  <UCard class="mb-8">
    <template #header>
      <PanelTitle
        title="Identity &amp; refresh"
        subtitle="When each half of the pipeline last touched this account."
      />
    </template>

    <UAlert
      v-if="identity.status === 'Invalid'"
      color="error"
      variant="subtle"
      icon="i-lucide-circle-slash"
      title="PUUID invalidated"
      description="account-v1 returns 404 for this PUUID and AccountRefresh could not recover the account by Riot ID — it was deleted, banned, or rotated with no usable Riot ID to look it up. The row is kept for history but excluded from every refresh and ingest selection, so nothing downstream will move again."
      class="mb-4"
    />

    <dl class="grid grid-cols-2 gap-x-4 gap-y-3 text-sm sm:grid-cols-3">
      <div>
        <dt class="text-muted text-xs uppercase mb-0.5">
          Status
        </dt>
        <dd>{{ identity.status }}</dd>
      </div>
      <div>
        <dt class="text-muted text-xs uppercase mb-0.5">
          Region
        </dt>
        <dd class="font-mono text-xs">
          {{ identity.platformId }}
        </dd>
      </div>
      <div>
        <dt class="text-muted text-xs uppercase mb-0.5">
          Rank score
        </dt>
        <dd class="tabular-nums">
          {{ formatNumber(identity.rankScore) }}
        </dd>
      </div>
      <div>
        <dt class="text-muted text-xs uppercase mb-0.5">
          First seen
        </dt>
        <dd class="tabular-nums">
          {{ traceStamp(identity.createdAtUtc) }}
        </dd>
      </div>
      <div>
        <dt class="text-muted text-xs uppercase mb-0.5">
          Identity synced
        </dt>
        <dd class="tabular-nums">
          {{ traceStamp(identity.lastProfileSyncAtUtc) }}
        </dd>
      </div>
      <div>
        <dt class="text-muted text-xs uppercase mb-0.5">
          Rank synced
        </dt>
        <dd class="tabular-nums">
          {{ traceStamp(identity.lastRankSyncAtUtc) }}
        </dd>
      </div>
      <div>
        <dt class="text-muted text-xs uppercase mb-0.5">
          Mains analysed
        </dt>
        <dd class="tabular-nums">
          {{ traceStamp(identity.lastMainCalcAtUtc) }}
        </dd>
      </div>
      <div>
        <dt class="text-muted text-xs uppercase mb-0.5">
          Activity checked
        </dt>
        <dd class="tabular-nums">
          {{ traceStamp(identity.lastActivityCheckAtUtc) }}
        </dd>
      </div>
      <div>
        <dt class="text-muted text-xs uppercase mb-0.5">
          Matches ingested
        </dt>
        <dd class="tabular-nums">
          {{ traceStamp(identity.lastMatchIngestAtUtc) }}
        </dd>
      </div>
    </dl>
  </UCard>
</template>
