// Single-add flow of the Accounts hub's "Add mains" tab (split out of
// `AccountsSeed.vue`, #1436): POST one Riot ID, then POLL
// `GET /api/ops/accounts/seed/{id}` every ~2s until a terminal status or a ~30s
// timeout, so the form can drive a live status stepper.
import type { Ref } from 'vue'
import type { SeedAccountBody, SeedRequestReadModel } from '~~/shared/types/ops'
import type { SeedTracking } from '~/utils/seed-tracking'
// Explicit imports rather than Nuxt auto-imports: unit tested outside the Nuxt
// context (`tests/seed-tracking`).
import { computed, onBeforeUnmount, shallowRef } from 'vue'
import { extractFetchError } from '~/utils/fetch-error'
import { seedStatusRead, trackedRequest } from '~/utils/seed-tracking'

export const SEED_POLL_INTERVAL_MS = 2000
export const SEED_POLL_TIMEOUT_MS = 30000

interface SeedTrackingHooks {
  /** A request was created (or found): the queue's first page now holds it. */
  onSubmitted: () => void
  /** The tracked request reached a terminal status: refresh the queue in place. */
  onSettled: () => void
}

/**
 * The single seed as one `SeedTracking` union (#2471). Every submit opens a new
 * generation and every poll loop belongs to one: a loop whose generation is over —
 * another submit, even of the same Riot ID (the API answers the same id), or the
 * component unmounting — stops at its next step instead of running alongside.
 */
export function useSeedTracking({ onSubmitted, onSettled }: SeedTrackingHooks) {
  const state = shallowRef<SeedTracking>({ s: 'idle' }) as Ref<SeedTracking>

  let generation = 0
  let pollTimer: ReturnType<typeof setTimeout> | null = null

  /** Ends whatever runs now: the pending timer and, through the generation, any read in flight. */
  function stop(): number {
    if (pollTimer) {
      clearTimeout(pollTimer)
      pollTimer = null
    }
    return ++generation
  }

  function schedule(id: string, mine: number, deadline: number) {
    pollTimer = setTimeout(() => pollOnce(id, mine, deadline), SEED_POLL_INTERVAL_MS)
  }

  async function pollOnce(id: string, mine: number, deadline: number) {
    pollTimer = null
    try {
      const next = await getSeedRequest(id)
      if (mine !== generation) {
        return
      }
      state.value = seedStatusRead(next)
      if (state.value.s === 'settled') {
        // The terminal request just landed — reflect it in the table too.
        onSettled()
        return
      }
    }
    catch {
      // Transient poll error: keep the last known state and retry until the
      // deadline. A persistent failure simply ends at the timeout branch.
      if (mine !== generation) {
        return
      }
    }

    if (Date.now() >= deadline) {
      const request = trackedRequest(state.value)
      if (request) {
        state.value = { s: 'timedOut', request }
      }
      return
    }
    schedule(id, mine, deadline)
  }

  async function submit(body: SeedAccountBody) {
    const mine = stop()
    state.value = { s: 'submitting' }

    try {
      const res = await seedAccount(body)
      if (mine !== generation) {
        return
      }
      // Seed the tracked request with what we know now; polling fills the rest.
      const request: SeedRequestReadModel = {
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
      state.value = seedStatusRead(request)
      onSubmitted()
      // Already terminal (an idempotent hit on an Ingested/Failed request)? No loop.
      if (state.value.s === 'settled') {
        onSettled()
        return
      }
      schedule(res.id, mine, Date.now() + SEED_POLL_TIMEOUT_MS)
    }
    catch (err: unknown) {
      if (mine !== generation) {
        return
      }
      // The inline alert is the whole report: a submit that fails leaves the form
      // on screen with its error in place, so a toast saying the same thing was
      // the second telling of a message the operator still has (#1661).
      state.value = { s: 'rejected', error: extractFetchError(err) }
    }
  }

  onBeforeUnmount(stop)

  return {
    state,
    /** The request on screen, whichever phase it is in. */
    tracked: computed(() => trackedRequest(state.value)),
    submitting: computed(() => state.value.s === 'submitting'),
    submit,
  }
}
