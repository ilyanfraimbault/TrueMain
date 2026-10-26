import type { Ref } from 'vue'
import type { ConflictCommand } from '~~/shared/utils/conflict-shortcuts'
import { defineShortcuts } from '@nuxt/ui/composables/defineShortcuts'
import { computed, ref, watch } from 'vue'
import { CONFLICT_SHORTCUTS, stepSelection } from '~~/shared/utils/conflict-shortcuts'

// Keyboard triage of the open conflict queue (#2293): `j`/`k` walk the rows, Enter
// opens the compare, one key per action inside it, Enter confirms an armed one, `?`
// shows the map. Built from `CONFLICT_SHORTCUTS`; Nuxt UI's `defineShortcuts` already
// ignores keys typed in an input.

export interface ConflictTriageOptions<T extends { id: string }> {
  rows: Ref<T[]>
  detailOpen: Ref<boolean>
  /** Keys are live: the open queue, not the history. */
  enabled: () => boolean
  /** An action key may fire: a loaded, still-open conflict, nothing in flight. */
  canAct: () => boolean
  /** An action waits for its confirmation. */
  armed: () => boolean
  open: (row: T) => void
  act: (actionKey: string) => void
  confirm: () => void
}

export function useConflictTriage<T extends { id: string }>(options: ConflictTriageOptions<T>) {
  const selected = ref(-1)
  const helpOpen = ref(false)

  watch(() => options.rows.value.length, (length) => {
    if (selected.value >= length) {
      selected.value = length - 1
    }
  })

  function select(row: { id: string }) {
    selected.value = options.rows.value.findIndex(r => r.id === row.id)
  }

  function run(command: ConflictCommand) {
    const rows = options.rows.value
    switch (command) {
      case 'help':
        helpOpen.value = !helpOpen.value
        return
      case 'next':
      case 'previous':
        selected.value = stepSelection(selected.value, rows.length, command === 'next' ? 1 : -1)
        return
      case 'open': {
        const row = rows[Math.max(selected.value, 0)]
        if (row && !options.detailOpen.value) {
          selected.value = Math.max(selected.value, 0)
          options.open(row)
        }
        return
      }
      case 'close':
        options.detailOpen.value = false
        return
      case 'confirm':
        options.confirm()
        return
      default:
        options.act(command)
    }
  }

  function live(scope: 'queue' | 'compare', command: ConflictCommand): boolean {
    if (command === 'help') {
      return true
    }
    if (helpOpen.value || !options.enabled()) {
      return false
    }
    if (scope === 'queue') {
      return !options.detailOpen.value
    }
    if (!options.detailOpen.value) {
      return false
    }
    if (command === 'close') {
      return true
    }
    if (command === 'confirm') {
      return options.armed() && options.canAct()
    }
    return options.canAct()
  }

  // Rebuilt on every state change, so a key that does not apply now is not bound
  // at all — Enter on a focused button keeps working when nothing is armed.
  defineShortcuts(computed(() => Object.fromEntries(
    CONFLICT_SHORTCUTS
      .filter(s => live(s.scope, s.command))
      .map(s => [s.key, () => run(s.command)]),
  )))

  return { selected, helpOpen, select }
}
