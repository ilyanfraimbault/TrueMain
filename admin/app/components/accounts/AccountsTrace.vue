<script setup lang="ts">
// Trace tab of the Accounts hub (#1410), formerly the standalone `/accounts`
// page. Account explorer (#1032) — start from a Riot ID, see what the pipeline did
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
//
// Each section is its own card component (`AccountsTrace*.vue`, #1436); this one
// owns the search and the one fetch, and hands each card its slice of the answer.
import type { AccountExplorer } from '~~/shared/types/ops'
import { RIOT_ID_MAX_LENGTH, isRiotIdOrSlug } from '~~/shared/utils/riot-id'

const route = useRoute()
const router = useRouter()

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
  // Keep whatever else is on the URL — the hub's `?view=` above all — so tracing
  // an account does not navigate the tab out from under the operator (#1410).
  // `undefined` values are dropped by the router, which is how "All regions"
  // clears the param.
  router.replace({ query: { ...route.query, riotId, region: selectedRegion.value } })
  load(riotId, selectedRegion.value)
}

// The hub's single navbar refresh button drives whichever tab is open; here that
// means re-running the current search (a no-op until one has been submitted).
defineExpose({ refresh: submit, pending })

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
</script>

<template>
  <!-- ============================= Search ============================ -->
  <UCard class="mb-8">
    <div class="flex flex-col gap-3">
      <PanelTitle title="Trace a Riot ID" subtitle="Read-only — every answer comes from the database.">
        <template #info>
          <p>
            This page never calls Riot, so it cannot tell an undiscovered account
            from a Riot ID that does not exist.
          </p>
          <p>
            A Riot ID is only unique within a routing region. Leave the region on
            "All regions" to see every account carrying it.
          </p>
        </template>
      </PanelTitle>
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
    <AccountsTraceVerdict :result="result" />
    <AccountsTraceIdentity v-if="result.identity" :identity="result.identity" />
    <AccountsTraceTracking
      v-if="result.tracking && result.matchesIngested"
      :tracking="result.tracking"
      :matches="result.matchesIngested"
    />
    <AccountsTraceCandidates
      v-if="result.identity"
      :candidates="result.candidates"
      :seed-request="result.seedRequest"
    />
    <AccountsTraceMains
      v-if="result.identity"
      :rows="result.mains.rows"
      :thresholds="result.mains.thresholds"
      :last-main-calc-at-utc="result.identity.lastMainCalcAtUtc"
    />
    <AccountsTraceRanks
      v-if="result.identity"
      :snapshots="result.rankSnapshots"
      :last-rank-sync-at-utc="result.identity.lastRankSyncAtUtc"
    />
  </template>
</template>
