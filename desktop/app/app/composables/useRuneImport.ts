import type { BuildRunePage } from '~/types/build'

/** What the shell answers when every rune page is the player's own (`runes.rs`). */
const NO_SLOT = 'no-slot'

/**
 * The rune import button's one call (#1678): the page on screen pushed into
 * the client as the TrueMain page, and selected. Only ever run on a click —
 * the shell's write path is built on that rule (`lcu::runes`).
 *
 * Every outcome is said in a toast: an import that fails silently is worse
 * than one that says why.
 */
export function useRuneImport() {
  const { state } = useLcuState()
  const toast = useToast()
  const pending = ref(false)
  const done = ref(false)
  let doneTimer: ReturnType<typeof setTimeout> | undefined

  /** The client has to be there to take the page. */
  const available = computed(() => state.value.connected)

  async function importRunes(page: BuildRunePage, champion: string) {
    if (pending.value) return
    pending.value = true
    try {
      if (insideTauri()) {
        const { invoke } = await import('@tauri-apps/api/core')
        await invoke('import_runes', { champion, page })
      }
      done.value = true
      clearTimeout(doneTimer)
      doneTimer = setTimeout(() => (done.value = false), 2500)
      toast.add({
        title: 'Runes imported',
        description: insideTauri() ? 'The TrueMain page is selected in the client.' : 'Dev server: nothing was sent to a client.',
        icon: 'i-lucide-check',
        color: 'success',
      })
    }
    catch (error) {
      const message = String(error)
      toast.add(message === NO_SLOT
        ? {
            title: 'No free rune page',
            description: 'Delete one of your rune pages in the client, then import again. TrueMain never deletes a page it did not create.',
            icon: 'i-lucide-triangle-alert',
            color: 'warning',
          }
        : {
            title: 'Could not import the runes',
            description: message,
            icon: 'i-lucide-circle-x',
            color: 'error',
          })
    }
    finally {
      pending.value = false
    }
  }

  onScopeDispose(() => clearTimeout(doneTimer))

  return { available, pending, done, importRunes }
}
