// Single-add flow of the Accounts hub's "Add mains" tab (split out of
// `AccountsSeed.vue`, #1436): POST one Riot ID, then POLL
// `GET /api/ops/accounts/seed/{id}` every ~2s until a terminal status or a ~30s
// timeout, so the form can drive a live status stepper.
import type { SeedAccountBody, SeedRequestReadModel, SeedRequestStatus } from '~~/shared/types/ops'
import { TERMINAL_SEED_STATUSES } from '~~/shared/types/ops'

export const SEED_POLL_INTERVAL_MS = 2000
export const SEED_POLL_TIMEOUT_MS = 30000

function isTerminal(status: SeedRequestStatus): boolean {
  return TERMINAL_SEED_STATUSES.includes(status)
}

interface SeedTrackingHooks {
  /** A request was created (or found): the queue's first page now holds it. */
  onSubmitted: () => void
  /** The tracked request reached a terminal status: refresh the queue in place. */
  onSettled: () => void
}

export function useSeedTracking({ onSubmitted, onSettled }: SeedTrackingHooks) {
  const submitting = ref(false)
  // The request currently being tracked (live status surfaces from this).
  const tracked = ref<SeedRequestReadModel | null>(null)
  // Set when the submit itself failed (network / 400) before we got an id.
  const submitError = ref<string | null>(null)
  // True while the poll loop is still running (non-terminal, pre-timeout).
  const polling = ref(false)
  // Flips true if we stop polling on the ~30s safety timeout rather than a
  // terminal status — the UI then says "still processing" instead of overclaiming.
  const polledOut = ref(false)

  let pollTimer: ReturnType<typeof setTimeout> | null = null
  let pollDeadline = 0

  function clearPoll() {
    if (pollTimer) {
      clearTimeout(pollTimer)
      pollTimer = null
    }
    polling.value = false
  }

  async function pollOnce(id: string) {
    try {
      const next = await getSeedRequest(id)
      // A newer submit may have replaced the tracked request mid-flight; ignore
      // stale responses so we never resurrect an old poll's result.
      if (tracked.value?.id !== id) {
        return
      }
      tracked.value = next
      if (isTerminal(next.status)) {
        clearPoll()
        // The terminal request just landed — reflect it in the table too.
        onSettled()
        return
      }
    }
    catch {
      // Transient poll error: keep the last known state and retry until the
      // deadline. A persistent failure simply ends at the timeout branch.
    }

    if (Date.now() >= pollDeadline) {
      polledOut.value = true
      clearPoll()
      return
    }
    pollTimer = setTimeout(() => pollOnce(id), SEED_POLL_INTERVAL_MS)
  }

  function startPolling(id: string) {
    clearPoll()
    polledOut.value = false
    // Already terminal (e.g. idempotent hit on an Ingested/Failed request)? Skip.
    if (tracked.value && isTerminal(tracked.value.status)) {
      onSettled()
      return
    }
    polling.value = true
    pollDeadline = Date.now() + SEED_POLL_TIMEOUT_MS
    pollTimer = setTimeout(() => pollOnce(id), SEED_POLL_INTERVAL_MS)
  }

  async function submit(body: SeedAccountBody) {
    submitError.value = null
    tracked.value = null
    polledOut.value = false
    clearPoll()
    submitting.value = true

    try {
      const res = await seedAccount(body)
      // Seed the tracked request with what we know now; polling fills the rest.
      tracked.value = {
        id: res.id,
        gameName: body.gameName,
        tagLine: body.tagLine,
        platformId: body.platformId,
        status: res.status,
        error: null,
        requestedAtUtc: new Date().toISOString(),
        processedAtUtc: null,
        resolvedPuuid: null,
        resolvedRiotAccountId: null,
      }
      onSubmitted()
      startPolling(res.id)
    }
    catch (err: unknown) {
      // The inline alert is the whole report: a submit that fails leaves the form
      // on screen with its error in place, so a toast saying the same thing was
      // the second telling of a message the operator still has (#1661).
      submitError.value = extractFetchError(err)
    }
    finally {
      submitting.value = false
    }
  }

  onBeforeUnmount(clearPoll)

  return { submitting, tracked, submitError, polling, polledOut, submit }
}
