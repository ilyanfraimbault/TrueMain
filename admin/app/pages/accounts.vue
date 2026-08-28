<script setup lang="ts">
// Account explorer (#1032) — start from a Riot ID, see what the pipeline did
// with it. The question "why does this player not show up on the site?" has many
// distinct answers, each living in a different table: never discovered,
// discovered but not tracked, tracked but its lease never came up, PUUID
// invalidated, retired by MainActivity, never promoted past the IsMain floor, or
// the games existed and retention pruned them.
//
// Two rules run through this page, and both come from the backend rather than
// being re-derived here:
//   - no state is inferred from an absent row without saying so, so every empty
//     section renders the sentence that explains it, never a bare 0 or a blank
//     table;
//   - every count carries the population it counts — the three "games" numbers
//     are three different populations and must never be shown as one.
// Read-only: no force-refresh, no re-queue.
import type { TableColumn } from '@nuxt/ui'
import type {
  AccountExplorer,
  AccountExplorerCandidate,
  AccountExplorerMainRow,
  AccountExplorerRankSnapshot,
  AccountPipelineState,
  BadgeColor,
} from '~~/shared/types/ops'
import { formatDateTime, formatNumber, formatTimeAgo } from '~~/shared/utils/format'
import { RIOT_ID_MAX_LENGTH, formatRiotId, isRiotIdOrSlug } from '~~/shared/utils/riot-id'

const route = useRoute()
const router = useRouter()
const { nameFor, iconFor } = useChampionStatic()

// =============================================================================
// Search — deep-linked, imperative
// =============================================================================
// `?riotId=&region=` is mirrored into the URL so a diagnosis can be pasted into
// an issue. The read is a one-shot `$fetch` rather than a reactive `useFetch`:
// there is nothing to fetch until an operator submits something.
const riotIdInput = ref('')
const regionInput = ref<string>(ALL)

const result = ref<AccountExplorer | null>(null)
const pending = ref(false)
const error = ref<unknown>(null)

const selectedRegion = computed(() => (regionInput.value === ALL ? undefined : regionInput.value))

// `GET /ops/accounts/{nameTag}` validates against `NameTagParser.TryParseRiotId`
// and 400s on anything else, so the same rules gate the input here (via
// `shared/utils/riot-id`) rather than letting the operator spend a round trip to
// be told the thing they pasted was never a Riot ID. Both typed `Name#TAG` and
// the hyphen slug `Name-TAG` are accepted, exactly as the endpoint does.
const searchable = computed(() => isRiotIdOrSlug(riotIdInput.value))

// Only nag once there is something to judge — an empty box is not "malformed".
const inputHint = computed(() => (
  !riotIdInput.value.trim() || searchable.value
    ? null
    : `Enter a Riot ID as Name#TAG or Name-TAG, at most ${RIOT_ID_MAX_LENGTH} characters.`
))

async function load(riotId: string, region: string | undefined) {
  pending.value = true
  error.value = null
  try {
    result.value = await getAccountExplorer(riotId, region)
  }
  catch (err) {
    // The endpoint never 404s — an unknown Riot ID is a populated answer — so
    // anything landing here is a real failure (400 on a malformed input, or the
    // backend being down) and deserves the error alert.
    error.value = err
    result.value = null
  }
  finally {
    pending.value = false
  }
}

function submit() {
  const riotId = riotIdInput.value.trim()
  if (!riotId || !searchable.value) {
    return
  }
  router.replace({ query: { riotId, ...(selectedRegion.value ? { region: selectedRegion.value } : {}) } })
  load(riotId, selectedRegion.value)
}

onMounted(() => {
  const riotId = typeof route.query.riotId === 'string' ? route.query.riotId : ''
  const region = typeof route.query.region === 'string' ? route.query.region : ''
  if (!riotId) {
    return
  }
  riotIdInput.value = riotId
  if (region) {
    regionInput.value = region
  }
  // A deep link carrying a malformed Riot ID gets the same treatment as a typed
  // one: the hint below the box explains it, instead of a 400 alert.
  if (!searchable.value) {
    return
  }
  load(riotId, region || undefined)
})

// =============================================================================
// Verdict
// =============================================================================
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

const identity = computed(() => result.value?.identity ?? null)
const tracking = computed(() => result.value?.tracking ?? null)
const matches = computed(() => result.value?.matchesIngested ?? null)
const mainRows = computed<AccountExplorerMainRow[]>(() => result.value?.mains.rows ?? [])
const thresholds = computed(() => result.value?.mains.thresholds ?? null)
const candidates = computed<AccountExplorerCandidate[]>(() => result.value?.candidates ?? [])
const rankSnapshots = computed<AccountExplorerRankSnapshot[]>(() => result.value?.rankSnapshots ?? [])

const riotIdLabel = computed(() => {
  const query = result.value?.query
  return (query && formatRiotId(query.gameName, query.tagLine)) ?? '—'
})

function formatPercent(rate: number | null | undefined, digits = 1): string {
  if (rate === null || rate === undefined || !Number.isFinite(rate)) {
    return '—'
  }
  return `${(rate * 100).toFixed(digits)}%`
}

// Both stamps in one cell: the absolute time answers "when", the relative one
// answers "is this recent", and an operator reading a stalled pipeline needs both.
function stamp(iso: string | null | undefined): string {
  return iso ? `${formatDateTime(iso)} (${formatTimeAgo(iso)})` : '—'
}

const claimAgeLabel = computed(() => {
  const seconds = tracking.value?.claimAgeSeconds
  if (seconds === null || seconds === undefined) {
    return '—'
  }
  return `${formatNumber(Math.round(seconds / 60))} min`
})

// =============================================================================
// Tables
// =============================================================================
const candidateColumns: TableColumn<AccountExplorerCandidate>[] = [
  { accessorKey: 'championId', header: 'Champion' },
  { accessorKey: 'status', header: 'Status' },
  { accessorKey: 'source', header: 'Source' },
  { accessorKey: 'score', header: 'Score' },
  { accessorKey: 'scoreInputs', header: 'Score inputs' },
  { accessorKey: 'discoveredAtUtc', header: 'Discovered' },
  { accessorKey: 'validatedAtUtc', header: 'Validated' },
]

const mainColumns: TableColumn<AccountExplorerMainRow>[] = [
  { accessorKey: 'championId', header: 'Champion' },
  { accessorKey: 'championMatches', header: 'Games' },
  { accessorKey: 'playRate', header: 'Play rate' },
  { accessorKey: 'flags', header: 'Flags' },
  { accessorKey: 'primaryPosition', header: 'Position' },
  { accessorKey: 'calculatedAtUtc', header: 'Analysed' },
]

const rankColumns: TableColumn<AccountExplorerRankSnapshot>[] = [
  { accessorKey: 'capturedAtUtc', header: 'Captured' },
  { accessorKey: 'tier', header: 'Rank' },
  { accessorKey: 'leaguePoints', header: 'LP' },
  { accessorKey: 'wins', header: 'W–L' },
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

function positionSummary(row: AccountExplorerMainRow): string {
  if (row.positionBreakdown.length === 0) {
    return row.primaryPosition || '—'
  }
  return row.positionBreakdown
    .map(position => `${position.position} ${formatPercent(position.rate, 0)}`)
    .join(' · ')
}

// The absence sentences. Each says what did not happen and, where the row set
// can tell us, when the responsible process last looked — "no rows" and "never
// ran" are different diagnoses and the page must not merge them.
const mainsEmptyNote = computed(() => {
  const lastRun = identity.value?.lastMainCalcAtUtc
  return lastRun
    ? `MainAnalysis has written no champion row for this account. It last ran on it ${stamp(lastRun)}, so the account was looked at and produced nothing above the sample floor.`
    : 'MainAnalysis has never run on this account, so the absence of champion rows is an absence of analysis — not a verdict that the player mains nothing.'
})

const candidatesEmptyNote = computed(() =>
  'No main_candidates row exists for this account. Note that candidates are keyed on (platformId, puuid) and carry no Riot ID of their own, so a candidate whose account has not been upserted yet would be invisible to this search.',
)

const rankEmptyNote = computed(() => {
  const lastSync = identity.value?.lastRankSyncAtUtc
  return lastSync
    ? `No rank snapshot on record. AccountRefresh last read league-v4 for this account ${stamp(lastSync)}, so the account was checked and came back unranked in solo queue.`
    : 'No rank snapshot on record, and AccountRefresh has never completed a league-v4 read for this account — so this is missing data, not a missing rank.'
})
</script>

<template>
  <UDashboardPanel id="accounts">
    <template #header>
      <UDashboardNavbar title="Account explorer" icon="i-lucide-user-search">
        <template #leading>
          <UDashboardSidebarCollapse />
        </template>
      </UDashboardNavbar>
    </template>

    <template #body>
      <!-- ============================= Search ============================ -->
      <UCard class="mb-8">
        <div class="flex flex-col gap-3">
          <div>
            <p class="text-sm font-medium text-highlighted">
              Trace a Riot ID
            </p>
            <p class="text-xs text-dimmed mt-0.5">
              Read-only. Every answer comes from the database — this page never calls
              Riot, so it cannot tell an undiscovered account from a Riot ID that does
              not exist.
            </p>
          </div>
          <div class="flex flex-wrap items-center gap-2">
            <UInput
              v-model="riotIdInput"
              icon="i-lucide-search"
              placeholder="Name#TAG"
              class="w-full sm:w-80"
              :loading="pending"
              @keydown.enter="submit"
            />
            <USelect
              v-model="regionInput"
              :items="REGION_ITEMS"
              icon="i-lucide-globe"
              placeholder="Region"
              class="w-40"
            />
            <UButton
              icon="i-lucide-arrow-right"
              color="neutral"
              variant="subtle"
              label="Trace"
              :disabled="!searchable"
              :loading="pending"
              @click="submit"
            />
          </div>
          <p v-if="inputHint" class="text-xs text-error">
            {{ inputHint }}
          </p>
          <p class="text-xs text-dimmed">
            A Riot ID is only unique within a routing region. Leave the region on
            "All regions" to see every account carrying it.
          </p>
        </div>
      </UCard>

      <FetchErrorAlert
        v-if="error"
        :error="error"
        title="Failed to trace this Riot ID"
        class="mb-8"
      />

      <USkeleton v-else-if="pending" class="h-[420px] w-full" />

      <div
        v-else-if="!result"
        class="flex h-[320px] items-center justify-center text-sm text-muted"
      >
        Enter a Riot ID above to trace it through the pipeline.
      </div>

      <template v-else>
        <!-- ============================ Verdict ========================== -->
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
            <p class="text-xs text-muted uppercase mb-2">
              Other accounts with this Riot ID
            </p>
            <p class="text-xs text-dimmed mb-2">
              The most recently active one is shown above. (gameName, tagLine,
              platformId) is deliberately not unique — Riot IDs are recyclable and
              collide across regions — so the others are listed rather than arbitrated
              away.
            </p>
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

        <!-- =========================== Identity ========================== -->
        <UCard v-if="identity" class="mb-8">
          <template #header>
            <p class="text-sm font-medium text-highlighted">
              Identity &amp; refresh
            </p>
            <p class="text-xs text-dimmed mt-0.5">
              When each half of the pipeline last touched this account.
            </p>
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
                {{ stamp(identity.createdAtUtc) }}
              </dd>
            </div>
            <div>
              <dt class="text-muted text-xs uppercase mb-0.5">
                Identity synced
              </dt>
              <dd class="tabular-nums">
                {{ stamp(identity.lastProfileSyncAtUtc) }}
              </dd>
            </div>
            <div>
              <dt class="text-muted text-xs uppercase mb-0.5">
                Rank synced
              </dt>
              <dd class="tabular-nums">
                {{ stamp(identity.lastRankSyncAtUtc) }}
              </dd>
            </div>
            <div>
              <dt class="text-muted text-xs uppercase mb-0.5">
                Mains analysed
              </dt>
              <dd class="tabular-nums">
                {{ stamp(identity.lastMainCalcAtUtc) }}
              </dd>
            </div>
            <div>
              <dt class="text-muted text-xs uppercase mb-0.5">
                Activity checked
              </dt>
              <dd class="tabular-nums">
                {{ stamp(identity.lastActivityCheckAtUtc) }}
              </dd>
            </div>
            <div>
              <dt class="text-muted text-xs uppercase mb-0.5">
                Matches ingested
              </dt>
              <dd class="tabular-nums">
                {{ stamp(identity.lastMatchIngestAtUtc) }}
              </dd>
            </div>
          </dl>
        </UCard>

        <!-- ====================== Tracking & ingestion ==================== -->
        <UCard v-if="tracking && matches" class="mb-8">
          <template #header>
            <p class="text-sm font-medium text-highlighted">
              Tracking &amp; ingestion
            </p>
            <p class="text-xs text-dimmed mt-0.5">
              There is no "tracked" column: membership is derived from the two arms of
              the ingest claim, exactly as the Ingestor derives it.
            </p>
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

          <p class="mt-3 text-xs text-dimmed">
            Compare the claim age against the Ingestor's
            <code>MatchIngestion:ClaimLeaseMinutes</code> (30 by default) to judge
            whether a run died holding the lease — the API cannot see that setting, so
            it reports the age rather than a verdict.
          </p>

          <template #footer>
            <p class="text-xs text-muted uppercase mb-3">
              Games on record — three populations, not three views of one number
            </p>
            <dl class="grid gap-4 sm:grid-cols-3">
              <div>
                <dt class="text-xs text-dimmed">
                  Participant rows
                </dt>
                <dd class="text-lg text-highlighted tabular-nums">
                  {{ formatNumber(matches.liveParticipantCount) }}
                </dd>
                <p class="text-xs text-dimmed mt-0.5">
                  Every champion, every queue — but deleted by retention. Covers
                  {{ formatDateTime(matches.oldestRetainedGameStartUtc) }} →
                  {{ formatDateTime(matches.newestRetainedGameStartUtc) }}.
                </p>
              </div>
              <div>
                <dt class="text-xs text-dimmed">
                  Career games (frozen aggregates)
                </dt>
                <dd class="text-lg text-highlighted tabular-nums">
                  {{ formatNumber(matches.careerGamesFromAggregates) }}
                </dd>
                <p class="text-xs text-dimmed mt-0.5">
                  Never deleted, but only ever folded <strong>main champions</strong> —
                  over {{ formatNumber(matches.aggregatedPatchCount) }} patch(es).
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
                  What the last MainAnalysis pass looked at, capped at 50. A ceiling,
                  not a total.
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

        <!-- ========================= Candidate funnel ===================== -->
        <UCard v-if="identity" :ui="{ body: 'p-0 sm:p-0' }" class="mb-8">
          <template #header>
            <p class="text-sm font-medium text-highlighted">
              Candidate funnel
            </p>
            <p class="text-xs text-dimmed mt-0.5">
              New → Scored → Queued → Processing → Validated (or Rejected).
            </p>
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

            <div class="border-t border-default px-4 py-3 space-y-1">
              <p class="text-xs text-dimmed">
                The score's <strong>components are not persisted</strong> — only the
                final blend is — so the inputs above are what can be shown. Recomputing
                recency / rank / points / scarcity here would fold today's champion
                coverage into a number produced against an older snapshot, and would
                silently disagree with the score beside it.
              </p>
              <p class="text-xs text-dimmed">
                A source of <code>Ladder</code> does not rule out a manual seed:
                ManualSeedProcess reuses the ladder upsert, so
                <code>ManualSeed</code> is never assigned in production. The seed
                request below is the reliable trail.
              </p>
            </div>
          </template>

          <template v-if="result.seedRequest" #footer>
            <p class="text-xs text-muted uppercase mb-2">
              Manual seed request
            </p>
            <div class="flex flex-wrap items-center gap-2 mb-2">
              <UBadge
                :color="seedStatusColor(result.seedRequest.status)"
                variant="subtle"
                size="sm"
                :icon="seedStatusIcon(result.seedRequest.status)"
                :label="result.seedRequest.status"
              />
              <span class="font-mono text-xs text-muted">{{ result.seedRequest.platformId }}</span>
              <span class="text-xs text-dimmed tabular-nums">
                requested {{ formatDateTime(result.seedRequest.requestedAtUtc) }} ·
                processed {{ formatDateTime(result.seedRequest.processedAtUtc) }}
              </span>
            </div>
            <UAlert
              v-if="result.seedRequest.error"
              color="error"
              variant="subtle"
              icon="i-lucide-triangle-alert"
              title="Seed request failed"
              :description="result.seedRequest.error"
            />
          </template>
        </UCard>

        <!-- ============================= Mains =========================== -->
        <UCard v-if="identity" :ui="{ body: 'p-0 sm:p-0' }" class="mb-8">
          <template #header>
            <p class="text-sm font-medium text-highlighted">
              Main champions
            </p>
            <p class="text-xs text-dimmed mt-0.5">
              What MainAnalysis computed, and what MainActivity did to it afterwards.
            </p>
          </template>

          <div v-if="mainRows.length === 0" class="px-4 py-8 text-sm text-muted">
            {{ mainsEmptyNote }}
          </div>

          <UTable
            v-else
            :data="mainRows"
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
              <span class="tabular-nums">{{ formatPercent(row.original.playRate) }}</span>
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
              <span class="text-xs text-muted">{{ positionSummary(row.original) }}</span>
            </template>
            <template #calculatedAtUtc-cell="{ row }">
              <span class="text-xs tabular-nums">{{ formatDateTime(row.original.calculatedAtUtc) }}</span>
            </template>
          </UTable>

          <template #footer>
            <div class="space-y-2">
              <p v-if="thresholds" class="text-xs text-dimmed">
                A champion is a main above a play rate somewhere between
                <span class="tabular-nums">{{ formatPercent(thresholds.playRateFloor, 0) }}</span> and
                <span class="tabular-nums">{{ formatPercent(thresholds.playRateThreshold, 0) }}</span>,
                and an OTP above
                <span class="tabular-nums">{{ formatPercent(thresholds.otpPlayRateThreshold, 0) }}</span>.
                {{ thresholds.effectiveThresholdNote }}
              </p>
              <p
                v-for="row in mainRows.filter(candidateRow => candidateRow.deactivation)"
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
              <p v-if="mainRows.some(row => row.analysisSkipped)" class="text-xs text-dimmed">
                "Not re-analysed" means MainAnalysis ran on the account more recently
                than it rewrote that row: its thin-sample guard declined to overwrite an
                established main from fewer than
                {{ formatNumber(thresholds?.minMatchesToEvaluate) }} matches. The row is
                deliberately old, not stale by accident.
              </p>
            </div>
          </template>
        </UCard>

        <!-- ========================= Rank snapshots ======================= -->
        <UCard v-if="identity" :ui="{ body: 'p-0 sm:p-0' }" class="mb-8">
          <template #header>
            <p class="text-sm font-medium text-highlighted">
              Rank snapshots
            </p>
            <p class="text-xs text-dimmed mt-0.5">
              Most recent first. Solo queue only, at most one row per UTC day, and never
              pruned — so a gap here is a gap in play, not in storage.
            </p>
          </template>

          <div v-if="rankSnapshots.length === 0" class="px-4 py-8 text-sm text-muted">
            {{ rankEmptyNote }}
          </div>

          <UTable
            v-else
            :data="rankSnapshots"
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
    </template>
  </UDashboardPanel>
</template>
