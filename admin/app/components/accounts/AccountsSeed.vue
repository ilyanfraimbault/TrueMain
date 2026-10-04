<script setup lang="ts">
// "Add mains" tab of the Accounts hub (#1410), formerly the standalone `/seed`
// page. Registers Riot IDs for ingestion via `POST /api/ops/accounts/seed`.
// It covers BOTH ways to add (they hit the same endpoint):
//
//   1. Single add — the 3-field form (gameName, tagLine, region). On submit we
//      get a request id back and POLL `GET /api/ops/accounts/seed/{id}` every
//      ~2s, driving a live status stepper (Pending -> Resolving ->
//      Ingested/Failed) until a terminal status or a ~30s timeout.
//   2. Bulk add — paste Riot IDs one per line as `gameName#tagLine` (optionally
//      `gameName#tagLine,REGION`). Parsing dedupes, flags malformed lines, and
//      renders a preview table; "Seed all (N)" POSTs every valid row with
//      limited concurrency, tracking per-row status + a progress bar + summary.
//
// A shared, server-paginated seed-request queue at the bottom reflects requests
// from BOTH the single and bulk flows (each submit jumps back to page 1 and
// refreshes). Paginated rather than capped at the newest rows because the weekly
// OTP seeder feeds this same queue in bulk (#1166).
//
// WORDING NOTE: "Ingested"/"queued" here means the account + mastery-derived
// candidates were created and queued — actual match ingestion + main
// classification happen on the NEXT Ingestor cycle, not synchronously.
//
// Each section is its own component since #1436 — <AccountsSeedSingle>,
// <AccountsSeedBulk> and <AccountsSeedQueue> — with the single-add polling and the
// bulk run state in `useSeedTracking` / `useBulkSeed`. This one wires the two
// forms to the queue.

interface SeedQueue { refresh: () => void, refreshFromFirstPage: () => void, pending: boolean }
const queue = ref<SeedQueue | null>(null)

// Submits jump the queue back to page 1 (the new request is there); the poller
// refreshes in place, since yanking the page out from under someone reading page
// 40 would be worse than a stale row.
function refreshQueue() {
  queue.value?.refresh()
}
function refreshQueueFromFirstPage() {
  queue.value?.refreshFromFirstPage()
}

// The hub's single navbar refresh button drives whichever tab is open; here that
// is the queue below the two forms.
const pending = computed(() => queue.value?.pending ?? false)
defineExpose({ refresh: refreshQueue, pending })
</script>

<template>
  <AccountsSeedSingle
    @submitted="refreshQueueFromFirstPage"
    @settled="refreshQueue"
  />

  <USeparator class="my-8" />

  <AccountsSeedBulk @finished="refreshQueueFromFirstPage" />

  <USeparator class="my-8" />

  <AccountsSeedQueue ref="queue" />
</template>
