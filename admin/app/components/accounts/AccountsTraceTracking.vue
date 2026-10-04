<script setup lang="ts">
// Tracking & ingestion card of the Trace tab: ingest-population membership, the
// lease, and the three "games" populations — never shown as one number. Split out
// of `AccountsTrace.vue` (#1436).
import type { AccountExplorerMatchesIngested, AccountExplorerTracking } from '~~/shared/types/ops'
import { formatDateTime, formatNumber } from '~~/shared/utils/format'

const props = defineProps<{
  tracking: AccountExplorerTracking
  matches: AccountExplorerMatchesIngested
}>()

const claimAgeLabel = computed(() => {
  const seconds = props.tracking.claimAgeSeconds
  if (seconds === null || seconds === undefined) {
    return '—'
  }
  return `${formatNumber(Math.round(seconds / 60))} min`
})
</script>

<template>
  <UCard class="mb-8">
    <template #header>
      <PanelTitle title="Tracking &amp; ingestion" subtitle="Membership is derived from the ingest claim, not stored.">
        <template #info>
          <p>
            There is no "tracked" column: membership is derived from the two arms
            of the ingest claim, exactly as the Ingestor derives it.
          </p>
          <p>
            Compare the claim age against the Ingestor's
            <code>MatchIngestion:ClaimLeaseMinutes</code> (30 by default) to judge
            whether a run died holding the lease — the API cannot see that
            setting, so it reports the age rather than a verdict.
          </p>
        </template>
      </PanelTitle>
    </template>

    <div class="flex flex-wrap items-center gap-2 mb-4">
      <UBadge
        :color="tracking.isTracked ? 'success' : 'neutral'"
        variant="subtle"
        size="sm"
        :icon="tracking.isTracked ? 'i-lucide-circle-check' : 'i-lucide-circle-off'"
        :label="tracking.isTracked ? `Tracked · ${tracking.trackedVia}` : 'Not in the ingest population'"
      />
      <UBadge
        :color="tracking.matchIngestStatus === 'Processing' ? 'warning' : 'neutral'"
        variant="subtle"
        size="sm"
        :label="`Lease ${tracking.matchIngestStatus}`"
      />
      <UBadge
        v-if="tracking.neverIngested"
        color="info"
        variant="subtle"
        size="sm"
        icon="i-lucide-hourglass"
        label="Never ingested"
      />
    </div>

    <dl class="grid grid-cols-2 gap-x-4 gap-y-3 text-sm sm:grid-cols-3">
      <div>
        <dt class="text-muted text-xs uppercase mb-0.5">
          Active main
        </dt>
        <dd>{{ tracking.hasActiveMain ? 'Yes' : 'No' }}</dd>
      </div>
      <div>
        <dt class="text-muted text-xs uppercase mb-0.5">
          Queued candidate
        </dt>
        <dd>{{ tracking.hasQueuedCandidate ? 'Yes' : 'No' }}</dd>
      </div>
      <div>
        <dt class="text-muted text-xs uppercase mb-0.5">
          Claim held for
        </dt>
        <dd class="tabular-nums">
          {{ claimAgeLabel }}
        </dd>
      </div>
    </dl>

    <template #footer>
      <PanelTitle
        variant="label"
        title="Games on record"
        subtitle="Three populations, not three views of one number."
        class="mb-3"
      >
        <template #info>
          <p>
            <strong>Participant rows</strong> — every champion, every queue, but
            deleted by retention.
          </p>
          <p>
            <strong>Career games</strong> — never deleted, but only ever folded
            <strong>main champions</strong>.
          </p>
          <p>
            <strong>Last analysis sample</strong> — what the last MainAnalysis
            pass looked at, capped at 50. A ceiling, not a total.
          </p>
        </template>
      </PanelTitle>
      <dl class="grid gap-4 sm:grid-cols-3">
        <div>
          <dt class="text-xs text-dimmed">
            Participant rows
          </dt>
          <dd class="text-lg text-highlighted tabular-nums">
            {{ formatNumber(matches.liveParticipantCount) }}
          </dd>
          <p class="text-xs text-dimmed mt-0.5 tabular-nums">
            {{ formatDateTime(matches.oldestRetainedGameStartUtc) }} →
            {{ formatDateTime(matches.newestRetainedGameStartUtc) }}
          </p>
        </div>
        <div>
          <dt class="text-xs text-dimmed">
            Career games (frozen aggregates)
          </dt>
          <dd class="text-lg text-highlighted tabular-nums">
            {{ formatNumber(matches.careerGamesFromAggregates) }}
          </dd>
          <p class="text-xs text-dimmed mt-0.5 tabular-nums">
            over {{ formatNumber(matches.aggregatedPatchCount) }} patch(es)
          </p>
        </div>
        <div>
          <dt class="text-xs text-dimmed">
            Last analysis sample
          </dt>
          <dd class="text-lg text-highlighted tabular-nums">
            {{ formatNumber(matches.lastAnalysisSampleSize) }}
          </dd>
          <p class="text-xs text-dimmed mt-0.5">
            capped at 50
          </p>
        </div>
      </dl>

      <UAlert
        v-if="matches.pruned"
        color="warning"
        variant="subtle"
        icon="i-lucide-eraser"
        title="Retention has deleted games for this account"
        :description="matches.prunedNote"
        class="mt-4"
      />
      <p v-else class="mt-4 text-xs text-dimmed">
        {{ matches.prunedNote }}
      </p>
    </template>
  </UCard>
</template>
