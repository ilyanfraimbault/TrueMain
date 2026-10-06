<script setup lang="ts">
// Pipeline tab of the Accounts hub (#1410) — the read-only view over
// `main_candidates`: New → Scored → Queued → Processing → Validated, on top
// of the throughput charts that say whether the funnel is
// moving at all. Searchable by Riot ID / PUUID / champion id, filterable by
// status + region, server-paged; a row click opens a slide-over with the exact
// pipeline stage, timestamps, ingested match count and the linked manual seed
// request (if one brought the account in).
//
// The seed-request queue used to be rendered here too, identically to the one on
// the seed page; it now lives once, on the "Add mains" tab (#1410).
// Read-only — no write actions (use "Add mains" to queue a request).
//
// Each card is its own component since #1436 — <AccountsPipelineThroughput>,
// <AccountsPipelineCandidates> and the <AccountsCandidateDetail> slide-over — and
// each card owns its fetches; this one wires them to the hub.
import type { CandidateDetail } from '~~/shared/types/ops'

// Both cards expose `refresh`/`pending`; `defineExpose` hands back a proxy that
// unwraps refs, which is what keeps the navbar spinner reactive.
interface PipelineCard { refresh: () => void, pending: boolean }
const throughput = ref<PipelineCard | null>(null)
const candidates = ref<PipelineCard | null>(null)

// --- Refresh every panel at once --------------------------------------------
// Exposed so the hub's single navbar refresh button drives whichever tab is open.
const anyPending = computed(() =>
  (candidates.value?.pending ?? false) || (throughput.value?.pending ?? false),
)
function refreshAll() {
  candidates.value?.refresh()
  throughput.value?.refresh()
}
defineExpose({ refresh: refreshAll, pending: anyPending })

// =============================================================================
// Candidate detail slide-over (Data Quality pattern: imperative + deep-linkable)
// =============================================================================
const {
  detailOpen,
  detail,
  detailPending,
  detailError,
  detailErrorTraceId,
  openDetail,
} = useDeepLinkedDetail<CandidateDetail>({
  queryKey: 'candidate',
  fetch: getCandidateDetail,
  notFoundMessage: id => `No candidate found with id "${id}".`,
  loadErrorMessage: 'Failed to load candidate detail.',
})
</script>

<template>
  <AccountsPipelineThroughput ref="throughput" />
  <AccountsPipelineCandidates ref="candidates" @select="openDetail" />
  <AccountsCandidateDetail
    v-model:open="detailOpen"
    :detail="detail"
    :pending="detailPending"
    :error="detailError"
    :error-trace-id="detailErrorTraceId"
  />
</template>
