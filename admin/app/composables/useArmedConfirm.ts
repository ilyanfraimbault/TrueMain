import type { Ref, WatchSource } from 'vue'
import type { ArmedConfirm } from '~/utils/armed-confirm'
// Explicit imports rather than Nuxt auto-imports: unit tested outside the Nuxt
// context (`tests/armed-confirm`).
import { computed, shallowRef, watch } from 'vue'
import { armedKey, pressArmed } from '~/utils/armed-confirm'

interface ArmedConfirmOptions {
  /**
   * The container's in-flight flag, for an action the container sends: clicks are
   * ignored while it is up, and its fall settles `sending`.
   */
  busy?: () => boolean
  /**
   * What the armed action is about (a persona, a conflict, a group…): when it
   * changes, nothing stays armed — a confirmation never carries over to another
   * entity. A send already in flight is left to settle.
   */
  scope?: WatchSource | WatchSource[]
}

/**
 * The two-click confirmation of the known-players actions (#2471) as one
 * `ArmedConfirm` union. Two ways to send:
 * - the component emits and its container calls the API: pass `busy`, call
 *   `press(key)` and emit when it answers true;
 * - the component calls the API itself: `run(key, call)` arms, then sends and
 *   settles around the call.
 */
export function useArmedConfirm<K extends string = string>(options: ArmedConfirmOptions = {}) {
  const state = shallowRef<ArmedConfirm<K>>({ s: 'idle' }) as Ref<ArmedConfirm<K>>

  /** A click on `key`; true when it sends. `confirm: false` sends on the first click. */
  function press(key: K, { confirm = true }: { confirm?: boolean } = {}): boolean {
    if (options.busy?.()) {
      return false
    }
    // A send the container never took up (its `busy` never rose) does not lock the buttons.
    const current = state.value.s === 'sending' && options.busy ? { s: 'idle' as const } : state.value
    const next = pressArmed(current, key, confirm)
    state.value = next.state
    return next.send
  }

  /** Arms, or sends `call` when `key` is already armed; settles once it is done. */
  async function run(key: K, call: () => Promise<unknown>, { confirm = true }: { confirm?: boolean } = {}): Promise<void> {
    if (!press(key, { confirm })) {
      return
    }
    try {
      await call()
    }
    finally {
      settle()
    }
  }

  /** Drops an armed action; a send in flight is left alone. */
  function disarm() {
    if (state.value.s === 'armed') {
      state.value = { s: 'idle' }
    }
  }

  /** The send is over, whatever its answer. */
  function settle() {
    state.value = { s: 'idle' }
  }

  if (options.busy) {
    const busy = options.busy
    watch(busy, (isBusy) => {
      if (!isBusy && state.value.s === 'sending') {
        settle()
      }
    })
  }
  if (options.scope) {
    watch(options.scope, disarm)
  }

  const armed = computed(() => armedKey(state.value))
  const sending = computed(() => state.value.s === 'sending' ? state.value.key : null)

  return {
    state,
    /** The key waiting for its second click, or `null`. */
    armed,
    /** The key sent and not settled yet, or `null`: the one button to spin. */
    sending,
    press,
    run,
    disarm,
    settle,
  }
}
