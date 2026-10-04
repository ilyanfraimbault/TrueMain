<script setup lang="ts">
// Verdict card of the Trace tab: the pipeline state the backend resolved for the
// Riot ID, in a badge and a sentence, plus the other accounts sharing that Riot ID.
// Split out of `AccountsTrace.vue` (#1436).
import type { AccountExplorer, AccountPipelineState, BadgeColor } from '~~/shared/types/ops'
import { formatDateTime, formatNumber } from '~~/shared/utils/format'
import { formatRiotId } from '~~/shared/utils/riot-id'

const props = defineProps<{
  result: AccountExplorer
}>()

const STATE_LABEL: Record<AccountPipelineState, string> = {
  NeverDiscovered: 'Never discovered',
  SeedRequestedOnly: 'Seed requested',
  Invalidated: 'Invalidated',
  Tracked: 'Tracked',
  Retired: 'Retired',
  NotAMain: 'Not a main',
  CandidateOnly: 'Candidate only',
  Discovered: 'Discovered',
}
const STATE_COLOR: Record<AccountPipelineState, BadgeColor> = {
  NeverDiscovered: 'neutral',
  SeedRequestedOnly: 'info',
  Invalidated: 'error',
  Tracked: 'success',
  Retired: 'warning',
  NotAMain: 'warning',
  CandidateOnly: 'info',
  Discovered: 'neutral',
}
const STATE_ICON: Record<AccountPipelineState, string> = {
  NeverDiscovered: 'i-lucide-circle-help',
  SeedRequestedOnly: 'i-lucide-clock',
  Invalidated: 'i-lucide-circle-slash',
  Tracked: 'i-lucide-circle-check',
  Retired: 'i-lucide-moon',
  NotAMain: 'i-lucide-minus-circle',
  CandidateOnly: 'i-lucide-list-ordered',
  Discovered: 'i-lucide-eye',
}

const identity = computed(() => props.result.identity)

const riotIdLabel = computed(() => {
  const query = props.result.query
  return (query && formatRiotId(query.gameName, query.tagLine)) ?? '—'
})
</script>

<template>
  <UCard class="mb-8">
    <div class="flex flex-col gap-3">
      <div class="flex flex-wrap items-center gap-3">
        <UBadge
          :color="STATE_COLOR[result.state]"
          variant="subtle"
          size="lg"
          :icon="STATE_ICON[result.state]"
          :label="STATE_LABEL[result.state]"
        />
        <span class="text-lg font-medium text-highlighted">{{ riotIdLabel }}</span>
        <span v-if="identity" class="font-mono text-xs text-muted">
          {{ identity.platformId }} · level {{ formatNumber(identity.summonerLevel) }}
        </span>
      </div>
      <p class="text-sm text-muted">
        {{ result.stateDetail }}
      </p>
      <p v-if="identity" class="font-mono text-xs text-dimmed break-all">
        {{ identity.puuid }}
      </p>
    </div>

    <template v-if="result.otherAccountsWithSameRiotId.length > 0" #footer>
      <PanelTitle
        variant="label"
        title="Other accounts with this Riot ID"
        subtitle="The most recently active one is shown above."
        info="(gameName, tagLine, platformId) is deliberately not unique — Riot IDs
          are recyclable and collide across regions — so the others are listed
          rather than arbitrated away."
        class="mb-2"
      />
      <ul class="space-y-1">
        <li
          v-for="other in result.otherAccountsWithSameRiotId"
          :key="other.riotAccountId"
          class="font-mono text-xs text-muted"
        >
          {{ other.platformId }} · {{ other.status }} ·
          last ingested {{ formatDateTime(other.lastMatchIngestAtUtc) }}
          <span class="text-dimmed">· {{ other.puuid }}</span>
        </li>
      </ul>
    </template>
  </UCard>
</template>
